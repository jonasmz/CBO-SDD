using AuthService.Application.Abstractions;
using AuthService.Domain.Sessions;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuthService.Infrastructure.Sessions;

public sealed class RefreshTokenStore(
    AuthDbContext dbContext,
    IOptions<SessionOptions> options,
    TimeProvider timeProvider) : IRefreshTokenStore, IUserSessionRevoker
{
    public async Task<IssuedRefreshToken> CreateSessionAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var rawToken = RefreshTokenGenerator.Generate();
        var token = RefreshToken.StartSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            userId,
            RefreshTokenGenerator.Hash(rawToken),
            now,
            TimeSpan.FromDays(options.Value.RefreshTokenDays),
            TimeSpan.FromDays(options.Value.SessionMaxDays));

        dbContext.RefreshTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new IssuedRefreshToken(rawToken, token.ExpiresAt);
    }

    public Task<RefreshToken?> FindByTokenAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return Task.FromResult<RefreshToken?>(null);
        }

        var hash = RefreshTokenGenerator.Hash(rawToken);
        return dbContext.RefreshTokens.SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
    }

    public async Task<RefreshTokenRotation> RotateAsync(
        RefreshToken current,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var rawToken = RefreshTokenGenerator.Generate();
        RefreshToken replacement;
        try
        {
            replacement = current.Rotate(
                now,
                Guid.NewGuid(),
                RefreshTokenGenerator.Hash(rawToken),
                TimeSpan.FromDays(options.Value.RefreshTokenDays));
        }
        catch (InvalidOperationException)
        {
            return new RefreshTokenRotation(RefreshTokenRotationStatus.NotUsable);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.RefreshTokens.Add(replacement);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RefreshTokenRotation(
                RefreshTokenRotationStatus.Succeeded,
                new IssuedRefreshToken(rawToken, replacement.ExpiresAt));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return new RefreshTokenRotation(RefreshTokenRotationStatus.ConcurrentReuse);
        }
    }

    public async Task RevokeFamilyAsync(
        Guid familyId,
        RefreshTokenRevocationReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        dbContext.ChangeTracker.Clear();
        await dbContext.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.State != RefreshTokenState.Revoked)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.State, RefreshTokenState.Revoked)
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.RevokedReason, reason),
                cancellationToken);
    }

    public async Task RevokeAllAsync(
        Guid userId,
        RefreshTokenRevocationReason reason,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        dbContext.ChangeTracker.Clear();
        await dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.State != RefreshTokenState.Revoked)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.State, RefreshTokenState.Revoked)
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.RevokedReason, reason),
                cancellationToken);
    }
}
