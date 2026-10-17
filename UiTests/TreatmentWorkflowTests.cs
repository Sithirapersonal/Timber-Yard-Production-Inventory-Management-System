using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using UiTests.Support;

namespace UiTests;

/// <summary>
/// Treatment workflow with seeded sawn stock: frontend + Treatment service + TreatmentDB.
/// Each test uses its own seeded species so tests cannot affect each other.
/// </summary>
[Collection("Treatment")]
public class TreatmentWorkflowTests : BaseTest
{
    private void OpenTreatment(Role role)
    {
        LoginAs(role);
        ClickSidebar("Treatment Process");
        WaitForHeading("Treatment & Chemical Batching");
    }

    // Batch History columns: 1 Batch, 2 Species, 3 Dimensions, 4 Chemical, 5 Quantity, 6 Status
    private By BatchRow(string species, string status) =>
        By.XPath($"//tbody/tr[td[2][normalize-space(.)='{species}'] and td[6][normalize-space(.)='{status}']]");

    private By StockRow(string species) =>
        By.XPath($"//tbody/tr[td[1][normalize-space(.)='{species}']]");

    private void WaitForRow(By row) => Wait.Until(d => d.FindElements(row).Any());

    private void RowButton(By row, string text) =>
        Wait.Until(d => d.FindElement(row).FindElements(By.XPath($".//button[normalize-space(.)='{text}']"))
            .FirstOrDefault(b => b.Displayed && b.Enabled))!.Click();

    private void CreateBatch(string species, string quantity)
    {
        ClickButton("+ Create Batch");
        WaitForText("New Treatment Batch");
        var select = new SelectElement(Field("Sawn Timber *"));
        Wait.Until(_ => select.Options.Any(o => o.Text.StartsWith(species + " ")));
        select.SelectByText(select.Options.First(o => o.Text.StartsWith(species + " ")).Text);
        Type("Quantity (m³) *", quantity);
        ClickButton("Create Batch");
    }

    private void StartBatch(string species, string tank)
    {
        RowButton(BatchRow(species, "Pending"), "Start Treatment");
        var dropdown = Wait.Until(d => d.FindElements(By.XPath(
            "//h3[normalize-space(.)='Start Treatment']/following::select[1]")).FirstOrDefault(e => e.Displayed))!;
        var s = new SelectElement(dropdown);
        Wait.Until(_ => s.Options.Any(o => o.Text.StartsWith(tank)));
        s.SelectByText(s.Options.First(o => o.Text.StartsWith(tank)).Text);
        ClickButton("Start Treatment"); // last match = the modal's button
    }

    private void CancelBatch(string species, string reason)
    {
        RowButton(BatchRow(species, "Pending"), "Cancel");
        WaitForText("Cancel Treatment Batch");
        Type("Cancellation Reason *", reason);
        ClickButton("Confirm Cancel");
        WaitForText("cancelled. Sawn stock restored.");
    }

    [Fact] // TRT-09
    public void TRT09_CreateBatch_AppearsAsPending_AndDeductsSawnStock()
    {
        OpenTreatment(Role.Manager);
        CreateBatch("E2E-Cedar", "1.5");

        WaitForText("Treatment batch created with status Pending.");
        WaitForRow(BatchRow("E2E-Cedar", "Pending"));

        ClickButton("Sawn Stock");
        Wait.Until(d => d.FindElement(StockRow("E2E-Cedar")).Text.Contains("8.5000")); // 10 - 1.5
    }

    [Fact] // TRT-10 full lifecycle across the UI, Treatment service and database
    public void TRT10_BatchLifecycle_CreateStartCompleteCreditsTreatedStock()
    {
        OpenTreatment(Role.Manager);
        CreateBatch("E2E-Teak", "1.5");
        WaitForRow(BatchRow("E2E-Teak", "Pending"));

        StartBatch("E2E-Teak", "TANK-B");
        WaitForText("started (InTreatment)");
        WaitForRow(BatchRow("E2E-Teak", "InTreatment"));

        ClickButton("Tank Schedule");
        Wait.Until(d => d.FindElement(StockRow("TANK-B")).Text.Contains("Busy"));

        ClickButton("Batch History");
        RowButton(BatchRow("E2E-Teak", "InTreatment"), "Mark Complete");
        WaitForText("Mark Batch Complete");
        ClickButton("Complete Batch");
        WaitForText("completed.");
        WaitForRow(BatchRow("E2E-Teak", "Completed"));

        ClickButton("Tank Schedule");
        Wait.Until(d => d.FindElement(StockRow("TANK-B")).Text.Contains("Idle"));

        ClickButton("Treated Stock");
        WaitForRow(StockRow("E2E-Teak"));

        // set a low-stock threshold on the treated stock row
        var row = Driver.FindElement(StockRow("E2E-Teak"));
        Overtype(row.FindElement(By.TagName("input")), "5");
        row.FindElement(By.XPath(".//button[normalize-space(.)='Save']")).Click();
        WaitForText("saved to 5 m³.");

        // the completed batch now feeds the duration report
        ClickButton("Reports");
        WaitForText("Average Turnaround by Chemical Type (hours)");
    }

    [Fact] // TRT-11
    public void TRT11_AdminCancelsPendingBatch_RestoresStockAndStoresReason()
    {
        OpenTreatment(Role.Admin);
        CreateBatch("E2E-Oak", "2");
        WaitForRow(BatchRow("E2E-Oak", "Pending"));

        CancelBatch("E2E-Oak", "E2E cancel reason");
        WaitForRow(BatchRow("E2E-Oak", "Cancelled"));

        Click(BatchRow("E2E-Oak", "Cancelled"));
        WaitForText("Batch Detail");
        Assert.Contains("E2E cancel reason", PageText);
        ClickButton("Close");

        ClickButton("Sawn Stock");
        Wait.Until(d => d.FindElement(StockRow("E2E-Oak")).Text.Contains("50.0000")); // fully restored
    }

    [Fact] // TRT-12
    public void TRT12_Manager_CanStartButNotCancelAPendingBatch()
    {
        OpenTreatment(Role.Manager);
        CreateBatch("E2E-Elm", "0.5");
        WaitForRow(BatchRow("E2E-Elm", "Pending"));

        var row = Driver.FindElement(BatchRow("E2E-Elm", "Pending"));
        Assert.NotEmpty(row.FindElements(By.XPath(".//button[normalize-space(.)='Start Treatment']")));
        Assert.Empty(row.FindElements(By.XPath(".//button[normalize-space(.)='Cancel']")));
    }

    [Fact] // TRT-13
    public void TRT13_RequestingMoreThanAvailableStock_IsRejected()
    {
        OpenTreatment(Role.Supervisor);
        CreateBatch("E2E-Pine", "6"); // only 5 m³ seeded

        WaitForText("Insufficient sawn stock. Available: 5 m³.");
        ClickButton("Batch History");
        WaitForText("Treatment Batch History");
        Assert.False(Exists(BatchRow("E2E-Pine", "Pending")), "no batch may be created when stock is insufficient");
    }

    [Fact] // TRT-14
    public void TRT14_BatchLargerThanTankCapacity_CannotStart()
    {
        OpenTreatment(Role.Admin);
        CreateBatch("E2E-Fir", "3"); // TANK-C holds 2.5 m³
        WaitForRow(BatchRow("E2E-Fir", "Pending"));

        StartBatch("E2E-Fir", "TANK-C");
        WaitForText("exceeds tank TANK-C capacity");
        ClickButton("Cancel"); // closes the start dialog (last match)

        Assert.True(Exists(BatchRow("E2E-Fir", "Pending")), "batch must stay Pending after a failed start");
        CancelBatch("E2E-Fir", "E2E cleanup"); // return the stock
    }
}