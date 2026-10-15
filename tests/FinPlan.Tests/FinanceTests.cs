using System.Text.Json;
using FinPlan;
using Xunit;

namespace FinPlan.Tests;

public sealed class FinanceTests
{
    private static Dataset Control() => new()
    {
        Rates = new() { ["RUB"] = "1", ["CHF"] = "110" },
        Accounts = [new("A-RUB", "RUB", 100), new("A-CHF", "CHF", 2)],
        End = new(2026, 10, 16),
        Obligations = [new() { Id = "O1", Currency = "RUB", Amount = 100, Direction = "out", Due = new(2026, 10, 12), ScheduledDate = new(2026, 10, 12) }, new() { Id = "O2", Currency = "RUB", Amount = 50, Direction = "in", Due = new(2026, 10, 16), ScheduledDate = new(2026, 10, 16) }],
        Operations = [new("T1", new(2026, 10, 10), "A-RUB", "RUB", "CP1", "Контрагент", "out", 40, "O1", "Частичная оплата", "supplier_payments")]
    };
    [Fact]
    public void PartialPaymentAndDatesMatchIndependentControl()
    {
        var d = Control(); FinanceCalculator.Calculate(d);
        Assert.Equal(60, d.Obligations[0].Outstanding); Assert.Equal("overdue", d.Obligations[0].Status);
        Assert.Equal(60, d.Calendar[0].Currencies["RUB"].Opening); Assert.Equal(0, d.Calendar[0].Currencies["RUB"].Closing);
        Assert.Equal(50, d.Calendar[1].Currencies["RUB"].Closing); Assert.Equal(220, d.Calendar[0].TotalRub); Assert.Equal(270, d.Calendar[1].TotalRub);
        Assert.Equal(60, d.Reports!.Currencies["RUB"]["closing"]);
    }
    [Fact]
    public void FullPaymentTakesPrecedenceOverOverdueAndFutureFactIsExcluded()
    {
        var d = Control(); d.Operations[0] = d.Operations[0] with { Amount = 100 };
        d.Operations.Add(d.Operations[0] with { Id = "FUTURE", Date = d.Cutoff });
        FinanceCalculator.Calculate(d);
        Assert.Equal("paid", d.Obligations[0].Status); Assert.False(d.Obligations[0].Overdue); Assert.Equal(0, d.Obligations[0].Outstanding);
        Assert.Equal(0, d.Reports!.Currencies["RUB"]["closing"]);
    }
    [Fact]
    public void ExcessivePaymentIsRejected()
    {
        var d = Control(); d.Operations[0] = d.Operations[0] with { Amount = 101 };
        Assert.Throws<InvalidDataException>(() => FinanceCalculator.Calculate(d));
    }
    [Fact]
    public void AccrualProfitIsIndependentOfPaymentsLoansAndEquipment()
    {
        var d = new Dataset { Accounts = [new("A-RUB", "RUB", 100)], Rates = new() { ["RUB"] = "1" } };
        (string Direction, decimal Amount, string Category)[] ops = [("out", 40, "supplier_payments"), ("in", 100, "loan_received"), ("out", 30, "equipment_purchase"), ("in", 50, "customer_receipts"), ("out", 20, "loan_repaid"), ("out", 10, "operating_payments")];
        d.Operations = ops.Select((t, i) => new Operation("T" + i, new(2026, 10, 10), "A-RUB", "RUB", "CP", "Компания", t.Direction, t.Amount, null, "Оплата", t.Category)).ToList();
        d.Accruals = [new("R", new(2026, 10, 8), "revenue", 200, "RUB", "Компания", "O1", "act.pdf"), new("C", new(2026, 10, 8), "cost_of_sales", 100, "RUB", "Компания", "O2", "act2.pdf"), new("O", new(2026, 10, 8), "operating_expenses", 20, "RUB", "Компания", "O3", "act3.pdf")];
        var v = FinanceCalculator.BuildReports(d).Currencies["RUB"];
        Assert.Equal(150, v["cash_in"]); Assert.Equal(100, v["cash_out"]); Assert.Equal(150, v["closing"]);
        Assert.Equal(0, v["operating_flow"]); Assert.Equal(-30, v["investing_flow"]); Assert.Equal(80, v["financing_flow"]); Assert.Equal(80, v["operating_profit"]);
        d.Operations[0] = d.Operations[0] with { Amount = 100 };
        Assert.Equal(80, FinanceCalculator.BuildReports(d).Currencies["RUB"]["operating_profit"]);
        d.Accruals.Add(d.Accruals[0]); Assert.Throws<InvalidDataException>(() => FinanceCalculator.BuildReports(d));
    }
    [Fact]
    public void SeedAndStatusesAreReproducible()
    {
        Assert.Equal(JsonSerializer.Serialize(DemoGenerator.Build(42), JsonFiles.Options), JsonSerializer.Serialize(DemoGenerator.Build(42), JsonFiles.Options));
        Assert.NotEqual(DemoGenerator.Build(42).Obligations[0].Amount, DemoGenerator.Build(43).Obligations[0].Amount);
        var d = DemoGenerator.Build(42); FinanceCalculator.Calculate(d);
        Assert.Equal(4, d.Obligations.Count(o => o.Status == "paid"));
        Assert.Contains(d.Obligations, o => o.Status == "overdue"); Assert.Contains(d.Obligations, o => o.Status == "in_progress");
        foreach (var day in d.Calendar) foreach (var v in day.Currencies.Values) Assert.Equal(v.Opening + v.In - v.Out, v.Closing);
    }
}

public sealed class GenerationVarietyTests
{
    [Fact]
    public void ValuesDatesAndPaymentFractionsHaveWideReproducibleSpread()
    {
        var d = DemoGenerator.Build(42); FinanceCalculator.Calculate(d);
        var rubEquivalent = d.Obligations.Select(o => o.Amount * decimal.Parse(d.Rates[o.Currency], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.True(rubEquivalent.Max() / rubEquivalent.Min() > 1000);
        Assert.True(d.Obligations.Select(o => o.Amount).Distinct().Count() > 300);
        Assert.True(d.Obligations.Count(o => o.Amount != decimal.Truncate(o.Amount)) > 200);
        Assert.Equal(new DateOnly(2026, 12, 31), d.End);
        Assert.True(d.Obligations.Max(o => o.ScheduledDate).DayNumber - d.Obligations.Min(o => o.ScheduledDate).DayNumber > 70);
        Assert.True(d.Obligations.Take(20).Select(o => o.RecognizedDate).Distinct().Count() > 10);
        Assert.True(d.Obligations.Take(20).Where(o => o.Paid > 0 && o.Outstanding > 0).Select(o => decimal.Round(o.Paid / o.Amount, 2)).Distinct().Count() > 3);
        Assert.Contains(d.Operations.GroupBy(t => t.Obligation).Where(g => g.Key is not null), group => group.Count() > 1);
        foreach (var c in d.Rates.Keys)
        {
            var balance = d.Accounts.Single(a => a.Currency == c).Opening;
            foreach (var t in d.Operations.Where(t => t.Currency == c).OrderBy(t => t.Date).ThenBy(t => t.Id))
            { balance += t.Direction == "in" ? t.Amount : -t.Amount; Assert.True(balance >= 0); }
        }
    }
}
