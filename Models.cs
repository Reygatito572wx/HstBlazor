namespace HstBlazor;

public sealed class TransactionRecord
{
    public string Id { get; set; } = "";
    public string User { get; set; } = "";
    public long Amount { get; set; }
    public DateTime Date { get; set; }
    public string Status { get; set; } = "Pendiente";
    public string Payment { get; set; } = "PSE";
    public string Recipient { get; set; } = "";
    public string Device { get; set; } = "Conocido";
    public string Location { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Risk { get; set; } = "Bajo";
    public string Factors { get; set; } = "";
}

public sealed class AlertRecord
{
    public string Id { get; set; } = "";
    public string TxId { get; set; } = "";
    public string Level { get; set; } = "Alto";
    public string Reason { get; set; } = "";
    public DateTime Date { get; set; }
    public string Status { get; set; } = "Activa";
}

public sealed record Requirement(string Id, string Name, string Description, string Priority, string Function);
public sealed class TestRecord(string id, string req, string input, string expected)
{
    public string Id { get; } = id;
    public string Requirement { get; } = req;
    public string Input { get; } = input;
    public string Expected { get; } = expected;
    public string Obtained { get; set; } = "";
    public double? Duration { get; set; }
    public string Status { get; set; } = "Sin ejecutar";
}
