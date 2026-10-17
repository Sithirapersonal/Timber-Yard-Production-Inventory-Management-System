using OpenQA.Selenium;
using UiTests.Pages;
using UiTests.Support;

namespace UiTests;

/// <summary>Login, logout and route protection. Services touched: frontend + Auth service + AuthDB.</summary>
public class AuthTests : BaseTest
{
    [Theory] // AUTH-01
    [InlineData(Role.Admin)]
    [InlineData(Role.Manager)]
    [InlineData(Role.Supervisor)]
    public void AUTH01_ValidLogin_ShowsDashboardForEveryRole(Role role)
    {
        LoginAs(role);

        Assert.Equal(BaseUrl + "/", Driver.Url);
        Assert.NotNull(SessionToken()); // JWT issued by the Auth service
        Assert.Contains(UserFor(role), PageText);
    }

    [Theory] // AUTH-02
    [InlineData("e2e_admin", "wrong-password")]
    [InlineData("no_such_user", "Whatever@1")]
    [InlineData("' OR '1'='1", "' OR '1'='1")] // injection-style input must not bypass login
    public void AUTH02_InvalidCredentials_ShowErrorAndStayOnLogin(string user, string pass)
    {
        var page = new LoginPage(Driver, Wait).Open(BaseUrl);
        page.LoginAs(user, pass);

        Assert.Equal("Invalid username or password.", page.WaitForError());
        Assert.EndsWith("/login", Driver.Url);
        Assert.Null(SessionToken());
    }

    [Fact] // AUTH-03
    public void AUTH03_EmptyForm_IsBlockedByRequiredValidation()
    {
        var page = new LoginPage(Driver, Wait).Open(BaseUrl);
        page.ClickSubmit();

        Assert.True(IsInvalid(page.Username));
        Assert.EndsWith("/login", Driver.Url);
    }

    [Fact] // AUTH-04
    public void AUTH04_MissingPassword_IsBlocked()
    {
        var page = new LoginPage(Driver, Wait).Open(BaseUrl);
        page.Username.SendKeys(UserFor(Role.Admin));
        page.ClickSubmit();

        Assert.True(IsInvalid(page.PasswordInput));
        Assert.EndsWith("/login", Driver.Url);
    }

    [Fact] // AUTH-05
    public void AUTH05_PasswordField_IsMasked()
    {
        var page = new LoginPage(Driver, Wait).Open(BaseUrl);
        Assert.Equal("password", page.PasswordInput.GetAttribute("type"));
    }

    [Theory] // AUTH-06
    [InlineData("/")]
    [InlineData("/log-intake")]
    [InlineData("/sawing")]
    [InlineData("/treatment")]
    [InlineData("/users")]
    public void AUTH06_ProtectedRoutes_RedirectToLoginWhenLoggedOut(string path)
    {
        GoTo(path);
        WaitForUrlEndsWith("/login");
    }

    [Fact] // AUTH-07
    public void AUTH07_Logout_ClearsSessionAndBlocksProtectedPages()
    {
        LoginAs(Role.Admin);
        Logout();

        WaitForUrlEndsWith("/login");
        Assert.Null(SessionToken());

        GoTo("/log-intake");
        WaitForUrlEndsWith("/login");
    }

    [Fact] // AUTH-08
    public void AUTH08_Login_IsRememberedAcrossPageReload()
    {
        LoginAs(Role.Manager);
        Driver.Navigate().Refresh();

        WaitForHeading("Dashboard");
        Assert.DoesNotContain("Staff Login", PageText);
    }
}