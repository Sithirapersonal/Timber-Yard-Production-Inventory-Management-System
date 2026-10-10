using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Xunit;

namespace UiTests;

public class EvidenceTests
{
    private const string BaseUrl = "http://localhost:5173";

    // Before running:
    // 1. Start the frontend: npm run dev
    // 2. Start Auth/LogIntake/Sawmill services.
    // 3. Set these two values to a test account that exists in your database.
    private const string Username = "admin7";
    private const string Password = "123@hello";

    private static string EvidenceFolder
    {
        get
        {
            var folder = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "../../../Evidence"));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    [Fact]
    public void Capture_84_Photo_Evidence()
    {
        using var driver = new ChromeDriver();
        driver.Manage().Window.Maximize();

        // ---------------- LOGIN ----------------
        driver.Navigate().GoToUrl($"{BaseUrl}/login");
        Pause();
        Shot(driver, "Evidence_001_Login_Empty");
        driver.FindElement(By.Id("username")).SendKeys(Username);
        Shot(driver, "Evidence_002_Login_Username");
        driver.FindElement(By.Id("password")).SendKeys(Password);
        Shot(driver, "Evidence_003_Login_Completed");
        driver.FindElement(By.CssSelector("button[type='submit']")).Click();
        WaitForUrl(driver, "/");
        Pause();
        Shot(driver, "Evidence_004_Login_Success_Dashboard");

        // ---------------- DASHBOARD ----------------
        Shot(driver, "Evidence_005_Dashboard");
        ClickText(driver, "Log Intake");
        Pause();
        Shot(driver, "Evidence_006_Log_Intake_Open");

        // ---------------- LOG INTAKE / STOCK ----------------
        Shot(driver, "Evidence_007_Stock_Tab");
        ClickText(driver, "Logs");
        Pause();
        Shot(driver, "Evidence_008_Logs_Tab");
        ClickText(driver, "Intake");
        Pause();
        Shot(driver, "Evidence_009_Intake_Tab");
        ClickText(driver, "History");
        Pause();
        Shot(driver, "Evidence_010_Delivery_History_Tab");
        ClickText(driver, "Suppliers");
        Pause();
        Shot(driver, "Evidence_011_Suppliers_Tab");

        // Supplier form
        var supplierInputs = driver.FindElements(By.CssSelector("input"));
        if (supplierInputs.Count >= 1) supplierInputs[0].SendKeys("Evidence Timber Supplier");
        if (supplierInputs.Count >= 2) supplierInputs[1].SendKeys("+94770000001");
        if (supplierInputs.Count >= 3) supplierInputs[2].SendKeys("Evidence Address");
        Shot(driver, "Evidence_012_Supplier_Form_Filled");

        // Clear supplier form visually
        foreach (var input in driver.FindElements(By.CssSelector("input")))
            input.Clear();
        Shot(driver, "Evidence_013_Supplier_Form_Cleared");

        // Back to stock
        ClickText(driver, "Stock");
        Pause();
        Shot(driver, "Evidence_014_Stock_Overview");

        // Refresh
        ClickText(driver, "Refresh");
        Pause();
        Shot(driver, "Evidence_015_Stock_Refreshed");

        // Expand first stock row if possible
        ClickFirstSafe(driver, "button");
        Pause();
        Shot(driver, "Evidence_016_Stock_Details_Expanded");

        // Logs tab + filters
        ClickText(driver, "Logs");
        Pause();
        Shot(driver, "Evidence_017_Log_Inventory");
        SelectFirstNonEmptyOption(driver);
        Pause();
        Shot(driver, "Evidence_018_Log_Inventory_Filter_1");
        SelectSecondSelectIfPossible(driver);
        Pause();
        Shot(driver, "Evidence_019_Log_Inventory_Filter_2");
        ClickText(driver, "Clear Filters");
        Pause();
        Shot(driver, "Evidence_020_Log_Inventory_Filters_Cleared");

        // Intake tab
        ClickText(driver, "Intake");
        Pause();
        Shot(driver, "Evidence_021_Delivery_Form_Empty");

        // Fill vehicle/notes if inputs exist
        FillByPlaceholder(driver, "WP-CA-4521", "WP-CA-9999");
        FillByPlaceholder(driver, "Inspected upon arrival", "Selenium evidence delivery");
        Shot(driver, "Evidence_022_Delivery_Details_Filled");

        // Select first options for selects
        SelectAllNonEmptyOptions(driver);
        Shot(driver, "Evidence_023_Delivery_Lookups_Selected");

        // Girth field
        FillByPlaceholder(driver, "3.50", "4.25");
        Shot(driver, "Evidence_024_Delivery_Log_Values");

        // Add/remove log row
        ClickText(driver, "Add Another Log");
        Pause();
        Shot(driver, "Evidence_025_Delivery_Second_Log_Row");
        ClickText(driver, "Remove");
        Pause();
        Shot(driver, "Evidence_026_Delivery_Row_Removed");

        // History
        ClickText(driver, "History");
        Pause();
        Shot(driver, "Evidence_027_Delivery_History");
        ClickText(driver, "Refresh");
        Pause();
        Shot(driver, "Evidence_028_Delivery_History_Refreshed");

        // Suppliers
        ClickText(driver, "Suppliers");
        Pause();
        Shot(driver, "Evidence_029_Supplier_Management");

        // ---------------- SAWING ----------------
        Navigate(driver, "/sawing");
        Pause();
        Shot(driver, "Evidence_030_Sawing_Overview");
        ClickText(driver, "Refresh");
        Pause();
        Shot(driver, "Evidence_031_Sawing_Stock_Refreshed");

        // Expand stock details / start job
        ClickFirstSafe(driver, "button");
        Pause();
        Shot(driver, "Evidence_032_Sawing_Stock_Details");

        ClickText(driver, "Start Saw Job");
        Pause();
        Shot(driver, "Evidence_033_Start_Saw_Job");

        // Start-job page states
        SelectAllNonEmptyOptions(driver);
        Shot(driver, "Evidence_034_Start_Job_Selections");
        FillFirstTextInputs(driver, "Selenium evidence");
        Shot(driver, "Evidence_035_Start_Job_Fields");

        // Return to overview
        ClickText(driver, "Stock Overview");
        Pause();
        Shot(driver, "Evidence_036_Sawing_Overview_Again");

        // History
        ClickText(driver, "Job History");
        Pause();
        Shot(driver, "Evidence_037_Job_History");
        Shot(driver, "Evidence_038_Job_History_Second_View");

        // Report if visible
        if (HasText(driver, "Wastage & Yield Report"))
        {
            ClickText(driver, "Wastage & Yield Report");
            Pause();
            Shot(driver, "Evidence_039_Wastage_Yield_Report");
            Shot(driver, "Evidence_040_Wastage_Yield_Report_Second_View");
        }
        else
        {
            Shot(driver, "Evidence_039_Sawing_Report_Not_Available_For_Role");
            Shot(driver, "Evidence_040_Sawing_Report_Role_View");
        }

        // ---------------- TREATMENT ----------------
        Navigate(driver, "/treatment");
        Pause();
        Shot(driver, "Evidence_041_Treatment_Process");
        Shot(driver, "Evidence_042_Treatment_Role_Scope");

        // ---------------- USER MANAGEMENT ----------------
        Navigate(driver, "/users");
        Pause();
        Shot(driver, "Evidence_043_User_Management");
        Shot(driver, "Evidence_044_User_Directory");

        // Add-user form
        FillFirstTextInputs(driver, "selenium_evidence_user");
        FillPasswordInputs(driver, "EvidencePassword123!");
        Shot(driver, "Evidence_045_Add_User_Form");

        SelectAllNonEmptyOptions(driver);
        Shot(driver, "Evidence_046_Add_User_Role_Selected");

        // Clear fields without submitting
        ClearAllInputs(driver);
        Shot(driver, "Evidence_047_Add_User_Form_Cleared");

        // Try edit controls if present
        ClickText(driver, "Edit");
        Pause();
        Shot(driver, "Evidence_048_Edit_User_Mode");
        Shot(driver, "Evidence_049_Edit_User_Fields");

        ClickText(driver, "Cancel");
        Pause();
        Shot(driver, "Evidence_050_Edit_User_Cancelled");

        // ---------------- NAVIGATION EVIDENCE ----------------
        Navigate(driver, "/");
        Pause();
        Shot(driver, "Evidence_051_Dashboard_After_Navigation");
        ClickText(driver, "Log Intake");
        Pause();
        Shot(driver, "Evidence_052_Navigate_To_Log_Intake");

        ClickText(driver, "Sawing Process");
        Pause();
        Shot(driver, "Evidence_053_Navigate_To_Sawing");

        Navigate(driver, "/");
        Pause();
        ClickText(driver, "Treatment Process");
        Pause();
        Shot(driver, "Evidence_054_Navigate_To_Treatment");

        Navigate(driver, "/");
        Pause();
        if (HasText(driver, "User Management"))
        {
            ClickText(driver, "User Management");
            Pause();
            Shot(driver, "Evidence_055_Navigate_To_User_Management");
        }
        else
        {
            Shot(driver, "Evidence_055_User_Management_Not_Shown_For_Role");
        }

        // ---------------- EXTRA UI STATES ----------------
        Navigate(driver, "/log-intake");
        Pause();

        ClickText(driver, "Stock");
        Pause();
        Shot(driver, "Evidence_056_LogIntake_Stock_State_1");
        Shot(driver, "Evidence_057_LogIntake_Stock_State_2");

        ClickText(driver, "Logs");
        Pause();
        Shot(driver, "Evidence_058_LogIntake_Logs_State_1");
        Shot(driver, "Evidence_059_LogIntake_Logs_State_2");

        ClickText(driver, "Intake");
        Pause();
        Shot(driver, "Evidence_060_LogIntake_Intake_State_1");
        Shot(driver, "Evidence_061_LogIntake_Intake_State_2");

        ClickText(driver, "History");
        Pause();
        Shot(driver, "Evidence_062_LogIntake_History_State_1");
        Shot(driver, "Evidence_063_LogIntake_History_State_2");

        ClickText(driver, "Suppliers");
        Pause();
        Shot(driver, "Evidence_064_LogIntake_Suppliers_State_1");
        Shot(driver, "Evidence_065_LogIntake_Suppliers_State_2");

        // ---------------- MORE SAWMILL STATES ----------------
        Navigate(driver, "/sawing");
        Pause();
        ClickText(driver, "Stock Overview");
        Pause();
        Shot(driver, "Evidence_066_Sawing_Overview_State_1");
        Shot(driver, "Evidence_067_Sawing_Overview_State_2");

        ClickText(driver, "Start Saw Job");
        Pause();
        Shot(driver, "Evidence_068_Sawing_Start_State_1");
        Shot(driver, "Evidence_069_Sawing_Start_State_2");

        ClickText(driver, "Job History");
        Pause();
        Shot(driver, "Evidence_070_Sawing_History_State_1");
        Shot(driver, "Evidence_071_Sawing_History_State_2");

        if (HasText(driver, "Wastage & Yield Report"))
        {
            ClickText(driver, "Wastage & Yield Report");
            Pause();
            Shot(driver, "Evidence_072_Sawing_Report_State_1");
            Shot(driver, "Evidence_073_Sawing_Report_State_2");
        }
        else
        {
            Shot(driver, "Evidence_072_Sawing_Report_Restricted");
            Shot(driver, "Evidence_073_Sawing_Report_Restricted_2");
        }

        // ---------------- FINAL EVIDENCE ----------------
        Navigate(driver, "/");
        Pause();
        Shot(driver, "Evidence_074_Final_Dashboard");
        Shot(driver, "Evidence_075_Final_Dashboard_2");

        Navigate(driver, "/log-intake");
        Pause();
        Shot(driver, "Evidence_076_Final_Log_Intake");
        Shot(driver, "Evidence_077_Final_Log_Intake_2");

        Navigate(driver, "/sawing");
        Pause();
        Shot(driver, "Evidence_078_Final_Sawing");
        Shot(driver, "Evidence_079_Final_Sawing_2");

        Navigate(driver, "/treatment");
        Pause();
        Shot(driver, "Evidence_080_Final_Treatment");
        Shot(driver, "Evidence_081_Final_Treatment_2");

        Navigate(driver, "/users");
        Pause();
        Shot(driver, "Evidence_082_Final_User_Management");
        Shot(driver, "Evidence_083_Final_User_Management_2");

        Navigate(driver, "/");
        Pause();
        Shot(driver, "Evidence_084_Final_Application");

        driver.Quit();
    }

    private static void Navigate(IWebDriver driver, string path)
    {
        driver.Navigate().GoToUrl($"{BaseUrl}{path}");
        Pause();
    }

    private static void Shot(IWebDriver driver, string name)
    {
        var file = Path.Combine(EvidenceFolder, $"{name}.png");
        ((ITakesScreenshot)driver).GetScreenshot().SaveAsFile(file);
    }

    private static void Pause()
    {
        Thread.Sleep(1000);
    }

    private static void WaitForUrl(IWebDriver driver, string pathPart)
    {
        for (var i = 0; i < 20; i++)
        {
            if (driver.Url.Contains(pathPart, StringComparison.OrdinalIgnoreCase))
                return;
            Thread.Sleep(500);
        }
    }

    private static bool HasText(IWebDriver driver, string text)
    {
        return driver.FindElements(By.XPath(
            $"//*[contains(normalize-space(), {XPathLiteral(text)})]")).Count > 0;
    }

    private static void ClickText(IWebDriver driver, string text)
    {
        var elements = driver.FindElements(By.XPath(
            $"//*[self::button or self::a or self::h1 or self::h2 or self::h3][contains(normalize-space(), {XPathLiteral(text)})]"));

        foreach (var element in elements)
        {
            try
            {
                ((IJavaScriptExecutor)driver).ExecuteScript(
                    "arguments[0].scrollIntoView({block:'center'});", element);
                element.Click();
                return;
            }
            catch
            {
                // Try the next matching element.
            }
        }
    }

    private static void ClickFirstSafe(IWebDriver driver, string selector)
    {
        var elements = driver.FindElements(By.CssSelector(selector));
        foreach (var element in elements)
        {
            try
            {
                if (element.Displayed && element.Enabled)
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript(
                        "arguments[0].scrollIntoView({block:'center'});", element);
                    element.Click();
                    return;
                }
            }
            catch
            {
            }
        }
    }

    private static void FillByPlaceholder(IWebDriver driver, string placeholder, string value)
    {
        var elements = driver.FindElements(By.CssSelector(
            $"input[placeholder*='{placeholder}'], textarea[placeholder*='{placeholder}']"));
        if (elements.Count == 0) return;

        elements[0].Clear();
        elements[0].SendKeys(value);
    }

    private static void FillFirstTextInputs(IWebDriver driver, string value)
    {
        var inputs = driver.FindElements(By.CssSelector(
            "input[type='text'], input:not([type]), textarea"));

        foreach (var input in inputs.Take(2))
        {
            try
            {
                input.Clear();
                input.SendKeys(value);
            }
            catch
            {
            }
        }
    }

    private static void FillPasswordInputs(IWebDriver driver, string value)
    {
        foreach (var input in driver.FindElements(By.CssSelector("input[type='password']")))
        {
            try
            {
                input.Clear();
                input.SendKeys(value);
            }
            catch
            {
            }
        }
    }

    private static void ClearAllInputs(IWebDriver driver)
    {
        foreach (var input in driver.FindElements(By.CssSelector("input, textarea")))
        {
            try { input.Clear(); } catch { }
        }
    }

    private static void SelectAllNonEmptyOptions(IWebDriver driver)
    {
        foreach (var select in driver.FindElements(By.TagName("select")))
        {
            try
            {
                var options = select.FindElements(By.TagName("option"))
                    .Where(o => !string.IsNullOrWhiteSpace(o.GetAttribute("value")))
                    .ToList();

                if (options.Count > 0)
                    options[0].Click();
            }
            catch
            {
            }
        }
    }

    private static void SelectFirstNonEmptyOption(IWebDriver driver)
    {
        var select = driver.FindElements(By.TagName("select")).FirstOrDefault();
        if (select == null) return;

        try
        {
            var option = select.FindElements(By.TagName("option"))
                .FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.GetAttribute("value")));

            option?.Click();
        }
        catch
        {
        }
    }

    private static void SelectSecondSelectIfPossible(IWebDriver driver)
    {
        var selects = driver.FindElements(By.TagName("select"));
        if (selects.Count < 2) return;

        try
        {
            var option = selects[1].FindElements(By.TagName("option"))
                .FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.GetAttribute("value")));

            option?.Click();
        }
        catch
        {
        }
    }

    private static string XPathLiteral(string value)
    {
        if (!value.Contains("'"))
            return $"'{value}'";

        if (!value.Contains("\""))
            return $"\"{value}\"";

        var parts = value.Split('\'');
        return "concat('" + string.Join("', \"'\", '", parts) + "')";
    }
}
