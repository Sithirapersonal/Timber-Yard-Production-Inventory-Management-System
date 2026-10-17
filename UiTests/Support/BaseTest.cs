using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using UiTests.Pages;

namespace UiTests.Support;

public enum Role { Admin, Manager, Supervisor }

/// <summary>
/// Shared setup for every E2E test: one fresh Chrome per test, explicit waits,
/// config from environment variables, login helper and small UI helpers.
/// </summary>
public abstract class BaseTest : IDisposable
{
    protected IWebDriver Driver { get; }
    protected WebDriverWait Wait { get; }

    // ---- configuration (override with environment variables) ----
    public static string BaseUrl => Env("E2E_BASE_URL", "http://localhost:5173").TrimEnd('/');
    public static string Password => Env("E2E_PASSWORD", "E2e@12345");

    protected static string UserFor(Role role) => role switch
    {
        Role.Admin => Env("E2E_ADMIN_USER", "e2e_admin"),
        Role.Manager => Env("E2E_MANAGER_USER", "e2e_manager"),
        _ => Env("E2E_SUPERVISOR_USER", "e2e_supervisor"),
    };

    protected static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

    protected BaseTest()
    {
        var options = new ChromeOptions();
        // Set E2E_HEADLESS=false to watch the browser (use this for the live demo).
        if (Env("E2E_HEADLESS", "true") != "false")
            options.AddArgument("--headless=new");
        options.AddArguments("--no-sandbox", "--disable-dev-shm-usage", "--window-size=1920,1080");

        Driver = new ChromeDriver(options);
        Wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(15));
    }

    public void Dispose()
    {
        Driver.Quit();
        GC.SuppressFinalize(this);
    }

    // ---- navigation / auth ----

    protected void GoTo(string path) => Driver.Navigate().GoToUrl(BaseUrl + path);

    protected void LoginAs(Role role)
    {
        new LoginPage(Driver, Wait).Open(BaseUrl).LoginAs(UserFor(role), Password);
        WaitForHeading("Dashboard");
    }

    protected void Logout() => ClickButton("Log Out");

    protected void WaitForHeading(string text) =>
        Wait.Until(d => d.FindElements(By.TagName("h1")).Any(h => h.Text.Trim() == text));

    protected void ClickSidebar(string label) =>
        Click(By.XPath($"//aside//a[normalize-space(.)='{label}']"));

    // ---- generic element helpers ----

    protected string PageText => Driver.FindElement(By.TagName("body")).Text;

    protected void WaitForText(string text) =>
        Wait.Until(d => d.FindElement(By.TagName("body")).Text.Contains(text));

    protected void WaitForUrlEndsWith(string suffix) =>
        Wait.Until(d => d.Url.TrimEnd('/').EndsWith(suffix));

    protected void Click(By by)
    {
        var el = Wait.Until(d => d.FindElements(by).LastOrDefault(e => e.Displayed && e.Enabled));
        el!.Click();
    }

    protected void ClickButton(string text) =>
        Click(By.XPath($"//button[normalize-space(.)='{text}']"));

    protected bool Exists(By by) => Driver.FindElements(by).Any(e => e.Displayed);

    protected bool ButtonVisible(string text) =>
        Exists(By.XPath($"//button[normalize-space(.)='{text}']"));

    /// <summary>Input/select that follows a label whose text contains <paramref name="label"/>.</summary>
    protected IWebElement Field(string label) =>
        Wait.Until(d => d.FindElements(By.XPath(
            $"//label[contains(normalize-space(.),'{label}')]/following-sibling::*[self::input or self::select][1]"))
            .FirstOrDefault(e => e.Displayed))!;

    protected void Type(string label, string value)
    {
        var f = Field(label);
        f.Clear();
        f.SendKeys(value);
    }

    /// <summary>Waits until the dropdown has real options (data loaded from the API), then picks the first one.</summary>
    protected string SelectFirstRealOption(IWebElement select)
    {
        var s = new SelectElement(select);
        Wait.Until(_ => s.Options.Count > 1);
        s.SelectByIndex(1);
        return s.SelectedOption.Text.Trim();
    }

    /// <summary>True when HTML5 validation (required / min / step) would block the field.</summary>
    protected bool IsInvalid(IWebElement el) =>
        !(bool)((IJavaScriptExecutor)Driver).ExecuteScript("return arguments[0].checkValidity();", el)!;

    protected object? SessionToken() =>
        ((IJavaScriptExecutor)Driver).ExecuteScript("return sessionStorage.getItem('token');");

    protected IAlert WaitForAlert() =>
        Wait.Until(d =>
        {
            try { return d.SwitchTo().Alert(); }
            catch (NoAlertPresentException) { return null; }
        })!;

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
}