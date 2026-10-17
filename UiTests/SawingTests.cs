using OpenQA.Selenium;
using UiTests.Support;

namespace UiTests;

/// <summary>Sawing module UI and role-based access (frontend + Sawmill service).</summary>
public class SawingTests : BaseTest
{
    private const string Heading = "Sawing Process & Stock Allocation";

    private void OpenSawing(Role role)
    {
        LoginAs(role);
        ClickSidebar("Sawing Process");
        WaitForHeading(Heading);
    }

    [Fact] // SAW-01
    public void SAW01_Page_LoadsWithHeadingAndDescription()
    {
        OpenSawing(Role.Manager);
        WaitForText("Start saw jobs, allocate raw logs, and track sawmill activity.");
    }

    [Theory] // SAW-02
    [InlineData(Role.Admin, true)]
    [InlineData(Role.Manager, true)]
    [InlineData(Role.Supervisor, false)]
    public void SAW02_Tabs_WastageReportOnlyForAdminAndManager(Role role, bool reportVisible)
    {
        OpenSawing(role);

        foreach (var tab in new[] { "Stock Overview", "+ Start Saw Job", "Job History" })
            Assert.True(ButtonVisible(tab), $"missing tab {tab}");

        Assert.Equal(reportVisible, ButtonVisible("Wastage & Yield Report"));
    }

    [Theory] // SAW-03
    [InlineData("Stock Overview", "Raw Stock Available for Sawing")]
    [InlineData("+ Start Saw Job", "Recently Started Jobs")]
    [InlineData("Job History", "Filter Job History")]
    public void SAW03_Tabs_ShowTheirContent(string tab, string expected)
    {
        OpenSawing(Role.Manager);
        ClickButton(tab);
        WaitForText(expected);
    }

    [Theory] // SAW-04
    [InlineData(Role.Admin)]
    [InlineData(Role.Manager)]
    public void SAW04_WastageYieldReport_OpensForAdminAndManager(Role role)
    {
        OpenSawing(role);
        ClickButton("Wastage & Yield Report");

        WaitForText("Filter by Date Range");
        Assert.DoesNotContain("does not have permission", PageText);
    }

    [Fact] // SAW-05
    public void SAW05_StartJobForm_ShowsAllFieldsAndSubmitButton()
    {
        OpenSawing(Role.Supervisor);
        ClickButton("+ Start Saw Job");
        WaitForText("Recently Started Jobs");

        foreach (var label in new[] { "Raw Stock Batch", "Allocated Machine", "Assigned Workers", "Notes" })
            Assert.Contains(label, PageText);
        Assert.True(ButtonVisible("Start Saw Job"));
    }

    [Theory] // SAW-06
    [InlineData(Role.Admin)]
    [InlineData(Role.Manager)]
    [InlineData(Role.Supervisor)]
    public void SAW06_StartJobTab_IsAvailableToEveryRole(Role role)
    {
        OpenSawing(role);
        ClickButton("+ Start Saw Job");

        WaitForText("Recently Started Jobs");
        Assert.DoesNotContain("does not have permission to start saw jobs", PageText);
    }
}