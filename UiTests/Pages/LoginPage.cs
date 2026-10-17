using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace UiTests.Pages;

/// <summary>Page Object for /login. Locators come from LoginPage.jsx (#username, #password, submit button).</summary>
public class LoginPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public LoginPage(IWebDriver driver, WebDriverWait wait)
    {
        _driver = driver;
        _wait = wait;
    }

    public IWebElement Username => _driver.FindElement(By.Id("username"));
    public IWebElement PasswordInput => _driver.FindElement(By.Id("password"));
    private IWebElement Submit => _driver.FindElement(By.CssSelector("button[type='submit']"));

    public LoginPage Open(string baseUrl)
    {
        _driver.Navigate().GoToUrl($"{baseUrl}/login");
        _wait.Until(d => d.FindElements(By.Id("username")).Any(e => e.Displayed));
        return this;
    }

    public void LoginAs(string user, string pass)
    {
        Username.SendKeys(user);
        PasswordInput.SendKeys(pass);
        Submit.Click();
    }

    public void ClickSubmit() => Submit.Click();

    /// <summary>The red error paragraph inside the login form.</summary>
    public string WaitForError() =>
        _wait.Until(d => d.FindElements(By.CssSelector("form p.text-rust")).FirstOrDefault(e => e.Displayed))!.Text;
}