using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using UiTests.Support;

namespace UiTests;

/// <summary>
/// Sawing "Start Saw Job" workflow with seeded raw stock (LogIntakeDB) and machines (SawmillDB).
/// Each test owns its species and machine, so tests cannot affect each other.
/// </summary>
[Collection("Sawing")]
public class SawingJobTests : BaseTest
{
    private const string Heading = "Sawing Process & Stock Allocation";
    private const string SeededWorkerCode = "EMP-011"; // Kamal Perera (seeded by Sawmill schema)

    private static readonly By LogBoxes =
        By.XPath("//label[starts-with(normalize-space(.),'LOG-')]//input[@type='checkbox']");

    /// <summary>Find + click inside the wait, so a re-render between the two (stale element) is retried.</summary>
    private void ClickStable(By by) =>
        Wait.Until(d =>
        {
            var el = d.FindElements(by).FirstOrDefault(e => e.Displayed && e.Enabled);
            if (el == null) return false;
            el.Click();
            return true;
        });

    private void OpenStartTab(Role role)
    {
        LoginAs(role);
        ClickSidebar("Sawing Process");
        WaitForHeading(Heading);
        ClickButton("+ Start Saw Job");
        WaitForText("Recently Started Jobs");
    }

    private void SelectBatch(string species)
    {
        var select = new SelectElement(Field("Raw Stock Batch"));
        Wait.Until(_ => select.Options.Any(o => o.Text.StartsWith(species + " ")));
        select.SelectByText(select.Options.First(o => o.Text.StartsWith(species + " ")).Text);
        Wait.Until(d => d.FindElements(LogBoxes).Count == 3); // 3 seeded logs
    }

    private void PickMachine(string code)
    {
        ClickButton("+ Allocate Machine");
        WaitForText("Select the machine this saw job will run on.");
        ClickStable(By.XPath($"//label[.//span[normalize-space(.)='{code}']]"));
        ClickButton("Confirm Selection");
        WaitForText($"({code})"); // chip text: "Name (CODE)"
    }

    private void AssignSeededWorker()
    {
        ClickButton("+ Assign Workers");
        ClickStable(By.XPath($"//label[.//span[normalize-space(.)='{SeededWorkerCode}']]"));
        ClickButton("Confirm (1)");
        Wait.Until(d => !d.FindElements(By.XPath("//h3[normalize-space(.)='Assign Workers']")).Any());
    }

    private IWebElement SubmitButton() =>
        Driver.FindElement(By.XPath("//button[normalize-space(.)='Start Saw Job']"));

    /// <summary>Full start flow; ends on the Start tab showing the new InProgress row.</summary>
    private void StartJob(Role role, string species, string machineCode)
    {
        OpenStartTab(role);
        SelectBatch(species);
        ClickButton("Select all");
        PickMachine(machineCode);
        AssignSeededWorker();
        ClickButton("Start Saw Job");
        WaitForText("Saw job started successfully!");
        ClickButton("+ Start Saw Job");
        WaitForText("Recently Started Jobs");
    }

    // Recent-jobs columns: 1 expander, 2 Job Code, 3 Volume, 4 Machine, 5 Workers, 6 Status, 7 Started
    private static string JobRowXPath(string machineCode, string statusLabel) =>
        $"//tbody/tr[td[4][contains(normalize-space(.),'{machineCode}')] " +
        $"and td[6][contains(normalize-space(.),'{statusLabel}')]]";

    private static By JobRow(string machineCode, string statusLabel) =>
        By.XPath(JobRowXPath(machineCode, statusLabel));

    private void WaitForRow(By row) => Wait.Until(d => d.FindElements(row).Any());

    private void CancelJob(string machineCode)
    {
        ClickStable(By.XPath(JobRowXPath(machineCode, "In Progress") +
                             "//button[normalize-space(.)='Cancel']"));
        WaitForText("Are you sure you want to cancel job");
        ClickButton("Confirm Cancel");
        WaitForText("cancelled.");
        WaitForRow(JobRow(machineCode, "Cancelled"));
    }

    [Fact] // SAW-07
    public void SAW07_StartJob_HappyPath_CreatesInProgressJob()
    {
        StartJob(Role.Manager, "E2E-SawA", "E2E-M1");

        WaitForRow(JobRow("E2E-M1", "In Progress"));
        var row = Driver.FindElement(JobRow("E2E-M1", "In Progress"));
        Assert.Contains("2.2500 m³", row.Text);   // 0.5 + 0.75 + 1.0 from the seeded logs
        Assert.Contains("Kamal Perera", row.Text);
    }

    [Fact] // SAW-08
    public void SAW08_StartButton_StaysDisabledUntilAllInputsChosen()
    {
        OpenStartTab(Role.Supervisor);
        Assert.False(SubmitButton().Enabled, "disabled with nothing selected");

        SelectBatch("E2E-SawB");
        ClickButton("Select all");
        Assert.False(SubmitButton().Enabled, "disabled without machine and worker");

        AssignSeededWorker();
        Assert.False(SubmitButton().Enabled, "disabled without machine");

        PickMachine("E2E-M2");
        Wait.Until(_ => SubmitButton().Enabled);
    }

    [Fact] // SAW-09
    public void SAW09_MachineUnderMaintenance_CannotBeSelected()
    {
        OpenStartTab(Role.Manager);
        ClickButton("+ Allocate Machine");
        WaitForText("Select the machine this saw job will run on.");

        var radio = By.XPath("//label[.//span[normalize-space(.)='MCH-03']]//input[@type='radio']");
        Wait.Until(d => d.FindElements(radio).Any());
        Assert.False(Driver.FindElement(radio).Enabled, "MCH-03 is UnderMaintenance and must be disabled");

        var available = By.XPath("//label[.//span[normalize-space(.)='MCH-01']]//input[@type='radio']");
        Assert.True(Driver.FindElement(available).Enabled);
    }

    [Fact] // SAW-10
    public void SAW10_SelectAllAndClear_UpdateInputQuantityReadout()
    {
        OpenStartTab(Role.Manager);
        WaitForText("0.0000 m³");

        SelectBatch("E2E-SawC");
        ClickButton("Select all");
        WaitForText("2.2500 m³");

        ClickButton("Clear");
        Wait.Until(d => d.FindElements(LogBoxes).All(b => !b.Selected));
        WaitForText("0.0000 m³");
    }

    [Fact] // SAW-11
    public void SAW11_Admin_CancelsInProgressJob()
    {
        StartJob(Role.Admin, "E2E-SawD", "E2E-M3");
        WaitForRow(JobRow("E2E-M3", "In Progress"));

        CancelJob("E2E-M3");

        Assert.False(Exists(JobRow("E2E-M3", "In Progress")));
    }

    [Fact] // SAW-12
    public void SAW12_CompleteJob_WithEmptyBoardRow_ShowsValidationMessage()
    {
        StartJob(Role.Manager, "E2E-SawE", "E2E-M4");
        WaitForRow(JobRow("E2E-M4", "In Progress"));

        ClickStable(By.XPath(JobRowXPath("E2E-M4", "In Progress") +
                             "//button[@title='Change job status']"));
        ClickButton("Complete");                       // dropdown item
        WaitForText("Enter the boards produced from this job");

        ClickButton("Mark Complete");                  // board row left empty
        WaitForText("Enter a valid length, width, thickness and quantity for every board row.");

        ClickButton("Cancel");                         // closes the modal (last match)
        Assert.True(Exists(JobRow("E2E-M4", "In Progress")), "job must stay In Progress");
    }

    [Fact] // SAW-13
    public void SAW13_PermanentDeleteButton_OnlyForAdmin()
    {
        StartJob(Role.Admin, "E2E-SawF", "E2E-M5");
        CancelJob("E2E-M5");

        var deleteBtn = By.XPath(".//button[@title='Permanently delete this cancelled job']");
        Assert.NotEmpty(Driver.FindElement(JobRow("E2E-M5", "Cancelled")).FindElements(deleteBtn));

        Logout();
        OpenStartTab(Role.Manager);
        WaitForRow(JobRow("E2E-M5", "Cancelled"));
        Assert.Empty(Driver.FindElement(JobRow("E2E-M5", "Cancelled")).FindElements(deleteBtn));
    }
}