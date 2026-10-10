using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Xunit;

namespace UiTests;

public class UnitTest1
{
    [Fact]
    public void CaptureEvidence()
    {
        using var driver = new ChromeDriver();

        driver.Manage().Window.Maximize();

        // 1. Home page
        driver.Navigate().GoToUrl("http://localhost:5173");
        Thread.Sleep(3000);

        var screenshot1 = ((ITakesScreenshot)driver).GetScreenshot();
        screenshot1.SaveAsFile("evidence-home.png");

        // 2. Login page
        driver.Navigate().GoToUrl("http://localhost:5173/login");
        Thread.Sleep(2000);

        var screenshot2 = ((ITakesScreenshot)driver).GetScreenshot();
        screenshot2.SaveAsFile("evidence-login.png");

        // 3. Supplier page
        driver.Navigate().GoToUrl("http://localhost:5173/suppliers");
        Thread.Sleep(2000);

        var screenshot3 = ((ITakesScreenshot)driver).GetScreenshot();
        screenshot3.SaveAsFile("evidence-supplier.png");

        // 4. Sawmill / Jobs page
        driver.Navigate().GoToUrl("http://localhost:5173/jobs");
        Thread.Sleep(2000);

        var screenshot4 = ((ITakesScreenshot)driver).GetScreenshot();
        screenshot4.SaveAsFile("evidence-jobs.png");

        driver.Quit();
    }
}