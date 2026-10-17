using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using UiTests.Pages;
using UiTests.Support;

namespace UiTests;

/// <summary>Admin-only user management (frontend + Auth service + AuthDB).</summary>
public class UserManagementTests : BaseTest
{
    private By Row(string username) =>
        By.XPath($"//h2[normalize-space(.)='User Directory']/following::tr[td[normalize-space(.)='{username}']][1]");

    private void OpenUsersPage()
    {
        LoginAs(Role.Admin);
        ClickSidebar("User Management");
        WaitForHeading("User Management");
        WaitForText("User Directory");
    }

    private void AddUser(string username, string password, string role)
    {
        Type("Username", username);
        Type("Password", password);
        new SelectElement(Field("Role")).SelectByText(role);
        ClickButton("Add User");
    }

    private void RemoveUser(string username)
    {
        if (!Exists(Row(username))) return;
        Driver.FindElement(Row(username)).FindElement(By.XPath(".//button[normalize-space(.)='Deactivate']")).Click();
        WaitForAlert().Accept();
        Wait.Until(d => !d.FindElements(Row(username)).Any());
    }

    [Fact] // UM-01
    public void UM01_Page_ShowsFormDirectoryAndAuditHistory()
    {
        OpenUsersPage();

        WaitForText("Add New Staff Account");
        WaitForText("Login / Audit History");
        Wait.Until(d => d.FindElements(Row(UserFor(Role.Admin))).Any());
    }

    [Theory] // UM-02
    [InlineData(Role.Manager)]
    [InlineData(Role.Supervisor)]
    public void UM02_NonAdmin_IsDeniedAccess(Role role)
    {
        LoginAs(role);
        GoTo("/users");

        WaitForText("Access denied");
        Assert.DoesNotContain("Add New Staff Account", PageText);
    }

    [Fact] // UM-03
    public void UM03_AddEditAndDeactivateUser_FullLifecycle()
    {
        var name = Unique("e2e_user");
        OpenUsersPage();
        try
        {
            AddUser(name, "Temp@12345", "Supervisor");
            WaitForText($"User \"{name}\" created successfully.");
            Wait.Until(d => d.FindElements(Row(name)).Any());

            // edit role -> Manager (the badge in the table shows "MGR")
            Driver.FindElement(Row(name)).FindElement(By.XPath(".//button[normalize-space(.)='Edit']")).Click();
            new SelectElement(Driver.FindElement(Row(name)).FindElement(By.TagName("select"))).SelectByText("Manager");
            Driver.FindElement(Row(name)).FindElement(By.XPath(".//button[normalize-space(.)='Save']")).Click();
            Wait.Until(d => d.FindElement(Row(name)).Text.Contains("MGR"));

            // deactivate
            RemoveUser(name);
            Assert.False(Exists(Row(name)));
        }
        finally { RemoveUser(name); }
    }

    [Fact] // UM-04
    public void UM04_DuplicateUsername_IsRejected()
    {
        var name = Unique("e2e_dup");
        OpenUsersPage();
        try
        {
            AddUser(name, "Temp@12345", "Supervisor");
            WaitForText($"User \"{name}\" created successfully.");

            AddUser(name, "Temp@12345", "Supervisor");
            var error = Wait.Until(d => d.FindElements(By.XPath(
                "//h2[normalize-space(.)='Add New Staff Account']/following::p[contains(@class,'text-rust')][1]"))
                .FirstOrDefault(e => e.Displayed && e.Text.Length > 0))!;
            Assert.False(string.IsNullOrWhiteSpace(error.Text));
        }
        finally { RemoveUser(name); }
    }

    [Fact] // UM-05
    public void UM05_AddForm_RequiresUsernameAndPassword()
    {
        OpenUsersPage();
        ClickButton("Add User");

        Assert.True(IsInvalid(Field("Username")));
        Assert.DoesNotContain("created successfully", PageText);
    }

    [Fact] // UM-06
    public void UM06_CancelEdit_RestoresRowActions()
    {
        OpenUsersPage();
        var admin = UserFor(Role.Admin);
        Wait.Until(d => d.FindElements(Row(admin)).Any());
        Driver.FindElement(Row(admin)).FindElement(By.XPath(".//button[normalize-space(.)='Edit']")).Click();
        Driver.FindElement(Row(admin)).FindElement(By.XPath(".//button[normalize-space(.)='Cancel']")).Click();

        Wait.Until(d => d.FindElement(Row(admin)).FindElements(By.XPath(".//button[normalize-space(.)='Edit']")).Any());
    }

    [Fact] // UM-07
    public void UM07_DismissingDeactivateConfirm_KeepsTheUser()
    {
        var name = Unique("e2e_keep");
        OpenUsersPage();
        try
        {
            AddUser(name, "Temp@12345", "Supervisor");
            Wait.Until(d => d.FindElements(Row(name)).Any());

            Driver.FindElement(Row(name)).FindElement(By.XPath(".//button[normalize-space(.)='Deactivate']")).Click();
            WaitForAlert().Dismiss();

            Assert.True(Exists(Row(name)));
        }
        finally { RemoveUser(name); }
    }

    [Fact] // UM-08
    public void UM08_FailedLogin_AppearsInAuditHistory()
    {
        var ghost = Unique("ghost");
        var login = new LoginPage(Driver, Wait).Open(BaseUrl);
        login.LoginAs(ghost, "Nope@12345");
        login.WaitForError();

        OpenUsersPage();
        var historyRow = By.XPath($"//h2[normalize-space(.)='Login / Audit History']/following::tr[td[normalize-space(.)='{ghost}']][1]");
        Wait.Until(d => d.FindElements(historyRow).Any());
        // textContent works even if the row is scrolled out of the history panel
        Assert.Contains("Failed", Driver.FindElement(historyRow).GetAttribute("textContent"));
    }
}