using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using UiTests.Support;

namespace UiTests;

/// <summary>Treatment module UI, validation rules and role-based tabs (frontend + Treatment service + TreatmentDB).</summary>
[Collection("Treatment")]
public class TreatmentTests : BaseTest
{
    private const string Heading = "Treatment & Chemical Batching";

    private void OpenTreatment(Role role)
    {
        LoginAs(role);
        ClickSidebar("Treatment Process");
        WaitForHeading(Heading);
    }

    [Fact] // TRT-01
    public void TRT01_Page_LoadsWithHeadingAndDescription()
    {
        OpenTreatment(Role.Manager);
        WaitForText("Create chemical treatment batches from sawn stock and track their status.");
    }

    [Theory] // TRT-02
    [InlineData(Role.Admin, true)]
    [InlineData(Role.Manager, true)]
    [InlineData(Role.Supervisor, false)]
    public void TRT02_Tabs_ReportsOnlyForAdminAndManager(Role role, bool reportsVisible)
    {
        OpenTreatment(role);

        foreach (var tab in new[] { "Sawn Stock", "+ Create Batch", "Batch History", "Tank Schedule", "Treated Stock" })
            Assert.True(ButtonVisible(tab), $"missing tab {tab}");

        Assert.Equal(reportsVisible, ButtonVisible("Reports"));
    }

    [Theory] // TRT-03
    [InlineData("Sawn Stock", "Sawn Stock Available for Treatment")]
    [InlineData("+ Create Batch", "New Treatment Batch")]
    [InlineData("Batch History", "Treatment Batch History")]
    [InlineData("Tank Schedule", "Tank Schedule & Utilization")]
    [InlineData("Treated Stock", "Treated Stock Inventory")]
    public void TRT03_Tabs_ShowTheirContent(string tab, string expected)
    {
        OpenTreatment(Role.Manager);
        ClickButton(tab);
        WaitForText(expected);
    }

    [Theory] // TRT-04
    [InlineData(Role.Admin)]
    [InlineData(Role.Manager)]
    public void TRT04_ReportsTab_OpensForAdminAndManager(Role role)
    {
        OpenTreatment(role);
        ClickButton("Reports");
        WaitForText("Treatment Turnaround & Duration Report");
    }

    [Fact] // TRT-05
    public void TRT05_CreateBatchForm_RequiresStockAndQuantity()
    {
        OpenTreatment(Role.Supervisor);
        ClickButton("+ Create Batch");
        WaitForText("New Treatment Batch");

        Assert.True(IsInvalid(Field("Sawn Timber *")));
        Assert.True(IsInvalid(Field("Quantity")));
        ClickButton("Create Batch");
        Assert.DoesNotContain("Treatment batch created", PageText);
    }

    [Fact] // TRT-06
    public void TRT06_QuantityField_EnforcesPositiveFourDecimalValues()
    {
        OpenTreatment(Role.Supervisor);
        ClickButton("+ Create Batch");
        WaitForText("New Treatment Batch");

        var quantity = Field("Quantity");
        foreach (var bad in new[] { "0", "-1", "0.00001" })
        {
            quantity.Clear();
            quantity.SendKeys(bad);
            Assert.True(IsInvalid(quantity), $"quantity {bad} should be invalid");
        }
        foreach (var good in new[] { "1.25", "0.0001", "10" })
        {
            quantity.Clear();
            quantity.SendKeys(good);
            Assert.False(IsInvalid(quantity), $"quantity {good} should be valid");
        }
    }

    [Fact] // TRT-07
    public void TRT07_ChemicalType_HasOptionsAndADefault()
    {
        OpenTreatment(Role.Supervisor);
        ClickButton("+ Create Batch");
        WaitForText("New Treatment Batch");

        var select = new SelectElement(Field("Chemical Type"));
        Assert.NotEmpty(select.Options);
        Assert.False(string.IsNullOrWhiteSpace(select.SelectedOption.Text));
    }

    [Fact] // TRT-08
    public void TRT08_TankSchedule_ListsTheSeededTanks()
    {
        OpenTreatment(Role.Manager);
        ClickButton("Tank Schedule");
        WaitForText("Tank Schedule & Utilization");

        // schema.sql seeds three tanks; PageText is used so no table markup is assumed
        foreach (var tank in new[] { "TANK-A", "TANK-B", "TANK-C" })
            WaitForText(tank);
    }
}