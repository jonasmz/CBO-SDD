using NetArchTest.Rules;

namespace AuthService.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_does_not_depend_on_infrastructure() => Assert.True(Types.InAssembly(typeof(AuthService.Domain.Roles.AppRoles).Assembly).ShouldNot().HaveDependencyOn("AuthService.Infrastructure").GetResult().IsSuccessful);
    [Fact]
    public void Domain_does_not_depend_on_AspNetCore_EFCore_or_Identity() => Assert.True(Types.InAssembly(typeof(AuthService.Domain.Roles.AppRoles).Assembly).ShouldNot().HaveDependencyOnAny("Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore.Identity").GetResult().IsSuccessful);
    [Fact]
    public void Application_does_not_depend_on_infrastructure() => Assert.True(Types.InAssembly(typeof(AuthService.Application.Features.Authentication.Login.LoginHandler).Assembly).ShouldNot().HaveDependencyOn("AuthService.Infrastructure").GetResult().IsSuccessful);
    [Fact]
    public void Application_does_not_depend_on_api() => Assert.True(Types.InAssembly(typeof(AuthService.Application.Features.Authentication.Login.LoginHandler).Assembly).ShouldNot().HaveDependencyOn("AuthService.Api").GetResult().IsSuccessful);
}
