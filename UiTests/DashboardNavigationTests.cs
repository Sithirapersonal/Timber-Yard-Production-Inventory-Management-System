using OpenQA.Selenium;
using UiTests.Support;

namespace UiTests;

/// <summary>Dashboard content, sidebar navigation and role-based menu visibility.</summary>
public class DashboardNavigationTests : BaseTest
{
    [Theory] // NAV-01
    [InlineData(Role.Admin)]
    [InlineData(Role.Manager)]
    [InlineData(Role.Supervisor)]
    public void NAV01_Dashboard_GreetsTheLoggedInUser(Role role)
    {
        LoginAs(role);
        WaitForText($"Welcome back, {UserFor(role)}.");
    }

    [Theory] // NAV-02
    [InlineData(Role.Admin, true)]
    [InlineData(Role.Manager, false)]
    [InlineData(Role.Supervisor, false)]
    public void NAV02_DashboardCards_UserManagementOnlyForAdmin(Role role, bool expectUserMgmt)
    {
        LoginAs(role);

        foreach (var card in new[] { "Log Intake", "Sawing Process", "Treatment Process" })
            Assert.True(Exists(By.XPath($"//main//h3[normalize-space(.)='{card}']")), $"missing card {card}");

        Assert.Equal(expectUserMgmt, Exists(By.XPath("//main//h3[normalize-space(.)='User Management']")));
    }

    [Theory] // NAV-03
    [InlineData("Log Intake", "Log Intake & Inventory Management")]
    [InlineData("Sawing Process", "Sawing Process & Stock Allocation")]
    [InlineData("Treatment Process", "Treatment & Chemical Batching")]
    public void NAV03_Sidebar_NavigatesToEachModule(string link, string heading)
    {
        LoginAs(Role.Manager);
        ClickSidebar(link);
        WaitForHeading(heading);
    }

    [Fact] // NAV-04
    public void NAV04_Sidebar_DashboardLinkReturnsHome()
    {
        LoginAs(Role.Manager);
        ClickSidebar("Sawing Process");
        WaitForHeading("Sawing Process & Stock Allocation");

        ClickSidebar("Dashboard");
        WaitForHeading("Dashboard");
    }

    [Theory] // NAV-05
    [InlineData(Role.Admin, true)]
    [InlineData(Role.Manager, false)]
    [InlineData(Role.Supervisor, false)]
    public void NAV05_Sidebar_UserManagementLinkOnlyForAdmin(Role role, bool expected)
    {
        LoginAs(role);
        Assert.Equal(expected, Exists(By.XPath("//aside//a[normalize-space(.)='User Management']")));
    }

    [Fact] // NAV-06
    public void NAV06_DashboardCard_OpensModule()
    {
        LoginAs(Role.Supervisor);
        Click(By.XPath("//main//h3[normalize-space(.)='Sawing Process']"));
        WaitForHeading("Sawing Process & Stock Allocation");
        Assert.EndsWith("/sawing", Driver.Url);
    }

    [Fact] // NAV-07
    public void NAV07_Topbar_ShowsUsernameAndLogOut()
    {
        LoginAs(Role.Manager);
        Assert.True(Exists(By.XPath($"//header//span[normalize-space(.)='{UserFor(Role.Manager)}']")));
        Assert.True(ButtonVisible("Log Out"));
    }
}