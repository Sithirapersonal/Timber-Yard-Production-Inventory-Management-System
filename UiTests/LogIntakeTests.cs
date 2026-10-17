using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using UiTests.Support;

namespace UiTests;

/// <summary>Log Intake module (frontend + LogIntake service + its MySQL schema).</summary>
public class LogIntakeTests : BaseTest
{
    private const string Heading = "Log Intake & Inventory Management";

    private void OpenLogIntake(Role role)
    {
        LoginAs(role);
        ClickSidebar("Log Intake");
        WaitForHeading(Heading);
    }

    private By SupplierRow(string name) =>
        By.XPath($"//h2[normalize-space(.)='All Suppliers']/following::tr[td[normalize-space(.)='{name}']][1]");

    private void CreateSupplier(string name, string phone = "+94771234567")
    {
        ClickButton("Suppliers");
        Type("Supplier Name", name);
        Type("Phone Number", phone);
        ClickButton("Add Supplier");
        WaitForText($"Supplier \"{name}\" created successfully!");
    }

    private void RowAction(string supplier, By button) =>
        Wait.Until(d => d.FindElement(SupplierRow(supplier)).FindElements(button).FirstOrDefault(b => b.Displayed))!.Click();

    private void RemoveSupplier(string name)
    {
        if (!Exists(SupplierRow(name))) return;
        if (Driver.FindElement(SupplierRow(name)).Text.Contains("Active"))
        {
            RowAction(name, By.XPath(".//button[normalize-space(.)='Deactivate']"));
            ClickButton("Confirm Deactivation");
            WaitForText("deactivated.");
        }
        RowAction(name, By.XPath(".//button[@title='Permanently delete this deactivated supplier']"));
        ClickButton("Permanently Delete");
        Wait.Until(d => !d.FindElements(SupplierRow(name)).Any());
    }

    [Fact] // LI-01
    public void LI01_Page_ShowsHeaderRoleAndFiveTabs()
    {
        OpenLogIntake(Role.Admin);

        Assert.Contains("Role: Admin", PageText);
        foreach (var tab in new[] { "Raw Stock Overview", "Log Inventory", "+ Record Delivery", "Delivery History", "Suppliers" })
            Assert.True(ButtonVisible(tab), $"missing tab {tab}");
    }

    [Theory] // LI-02
    [InlineData("Raw Stock Overview", "Current Stock by Species & Length")]
    [InlineData("Log Inventory", "In-Stock Individual Logs")]
    [InlineData("+ Record Delivery", "Record New Timber Delivery")]
    [InlineData("Delivery History", "Timber Delivery History")]
    [InlineData("Suppliers", "All Suppliers")]
    public void LI02_Tabs_ShowTheirContent(string tab, string expected)
    {
        OpenLogIntake(Role.Manager);
        ClickButton(tab);
        WaitForText(expected);
    }

    [Theory] // LI-03
    [InlineData(Role.Admin, true)]
    [InlineData(Role.Manager, false)]
    [InlineData(Role.Supervisor, false)]
    public void LI03_AddSupplierForm_OnlyForAdmin(Role role, bool expected)
    {
        OpenLogIntake(role);
        ClickButton("Suppliers");
        WaitForText("All Suppliers");

        Assert.Equal(expected, PageText.Contains("Add New Supplier"));
    }

    [Fact] // LI-04
    public void LI04_CreateSupplier_AppearsInListAsActive()
    {
        var name = Unique("E2E Supplier");
        OpenLogIntake(Role.Admin);
        try
        {
            CreateSupplier(name);
            Wait.Until(d => d.FindElements(SupplierRow(name)).Any());
            Assert.Contains("Active", Driver.FindElement(SupplierRow(name)).Text);
        }
        finally { RemoveSupplier(name); }
    }

    [Fact] // LI-05
    public void LI05_SupplierLifecycle_DeactivateReactivateDelete()
    {
        var name = Unique("E2E Supplier");
        OpenLogIntake(Role.Admin);
        CreateSupplier(name);

        RowAction(name, By.XPath(".//button[normalize-space(.)='Deactivate']"));
        ClickButton("Confirm Deactivation");
        WaitForText($"Supplier \"{name}\" deactivated.");
        Wait.Until(d => d.FindElement(SupplierRow(name)).Text.Contains("Deactivated"));

        RowAction(name, By.XPath(".//button[normalize-space(.)='Reactivate']"));
        ClickButton("Reactivate"); // modal confirm button (last visible match)
        WaitForText($"Supplier \"{name}\" reactivated.");

        RemoveSupplier(name);
        Assert.False(Exists(SupplierRow(name)));
    }

    [Fact] // LI-06
    public void LI06_SupplierForm_RequiresNameAndPhone()
    {
        OpenLogIntake(Role.Admin);
        ClickButton("Suppliers");
        WaitForText("Add New Supplier");

        Assert.True(IsInvalid(Field("Supplier Name")));
        Assert.True(IsInvalid(Field("Phone Number")));
        ClickButton("Add Supplier");
        Assert.DoesNotContain("created successfully", PageText);
    }

        [Fact] // LI-07
        public void LI07_DeliveryForm_BlocksSubmitWithoutSupplierAndGirth()
        {
            OpenLogIntake(Role.Supervisor);
            ClickButton("+ Record Delivery");
            WaitForText("Record New Timber Delivery");

            // species and length are pre-selected once their lists load, so only supplier and girth start empty
            Wait.Until(_ => new SelectElement(Field("Species *")).Options.Count > 1);
            Assert.False(IsInvalid(Field("Species *")), "species should be pre-selected");
            Assert.False(IsInvalid(Field("Length (ft) *")), "length should be pre-selected");

            Assert.True(IsInvalid(Field("Supplier *")));
            Assert.True(IsInvalid(Field("Girth (ft) *")));
            Click(By.CssSelector("form button[type='submit']"));
            Assert.DoesNotContain("Delivery recorded successfully", PageText);
        }

     [Fact] // LI-08
     public void LI08_DeliveryForm_RejectsZeroAndNegativeGirth()
     {
         OpenLogIntake(Role.Supervisor);
         ClickButton("+ Record Delivery");
         WaitForText("Record New Timber Delivery");

         var girth = Field("Girth (ft) *");
         foreach (var bad in new[] { "0", "-2.5" })
         {
             Overtype(girth, bad);
             Assert.True(IsInvalid(girth), $"girth {bad} should be invalid");
         }
         Overtype(girth, "3.5");
         Assert.False(IsInvalid(girth), "girth 3.5 should be valid");
     }

    [Fact] // LI-09
    public void LI09_DeliveryForm_AddAndRemoveLogRows()
    {
        OpenLogIntake(Role.Supervisor);
        ClickButton("+ Record Delivery");
        WaitForText("Record New Timber Delivery");

        By logRows = By.XPath("//span[starts-with(normalize-space(.),'Log #')]");
        Assert.Single(Driver.FindElements(logRows));
        Assert.False(Driver.FindElement(By.XPath("//button[normalize-space(.)='Remove']")).Enabled, "last row must not be removable");

        ClickButton("+ Add Another Log");
        Wait.Until(d => d.FindElements(logRows).Count == 2);

        Driver.FindElements(By.XPath("//button[normalize-space(.)='Remove']")).First().Click();
        Wait.Until(d => d.FindElements(logRows).Count == 1);
    }

    [Fact] // LI-10 (cross-service happy path)
    public void LI10_RecordDelivery_SucceedsAndAppearsInHistory()
    {
        var supplier = Unique("E2E Supplier");
        OpenLogIntake(Role.Admin);
        CreateSupplier(supplier);

        ClickButton("+ Record Delivery");
        WaitForText("Record New Timber Delivery");
        var supplierSelect = Field("Supplier *");
        Wait.Until(_ => new SelectElement(supplierSelect).Options.Any(o => o.Text.Trim() == supplier));
        new SelectElement(supplierSelect).SelectByText(supplier);
        Type("Vehicle Plate", "E2E-1234");
        SelectFirstRealOption(Field("Species *"));
        SelectFirstRealOption(Field("Length (ft) *"));
        Type("Girth (ft) *", "3.5");
        Click(By.CssSelector("form button[type='submit']"));

        WaitForText("Delivery recorded successfully with 1 logs!");

        ClickButton("Delivery History");
        WaitForText("Timber Delivery History");
        WaitForText(supplier);
        // supplier now has a delivery, so it is intentionally left in place (CI database is disposable)
    }

    [Fact] // LI-11
    public void LI11_StockOverview_RefreshKeepsTableVisible()
    {
        OpenLogIntake(Role.Manager);
        WaitForText("Current Stock by Species & Length");
        ClickButton("Refresh");
        WaitForText("Current Stock by Species & Length");
        Assert.DoesNotContain("Network error", PageText);
    }

    [Fact] // LI-12
    public void LI12_LogDeleteAction_NotAvailableToSupervisor()
    {
        OpenLogIntake(Role.Supervisor);
        ClickButton("Log Inventory");
        WaitForText("In-Stock Individual Logs");
        Assert.False(ButtonVisible("Delete"), "only Admin may delete logs");
    }
}