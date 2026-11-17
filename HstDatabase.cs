using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HstBlazor;

/// <summary>Persistencia SQLite del prototipo HST.</summary>
public sealed class HstDatabase(IWebHostEnvironment env, IConfiguration configuration)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string JsonPath => Path.Combine(env.ContentRootPath, "hst-data.json");
    private string ConnectionString
    {
        get
        {
            var configured = configuration.GetConnectionString("HstDatabase") ?? "Data Source=hst.db";
            var builder = new SqliteConnectionStringBuilder(configured);
            if (!Path.IsPathRooted(builder.DataSource)) builder.DataSource = Path.Combine(env.ContentRootPath, builder.DataSource);
            return builder.ToString();
        }
    }

    private sealed class LegacyStore
    {
        public List<TransactionRecord> Transactions { get; set; } = [];
        public List<AlertRecord> Alerts { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static LegacyStore Seed() => new()
    {
        Transactions = [
            new(){Id="HST-2025-001",User="Laura Gómez",Amount=245000,Date=DateTime.Parse("2025-05-22T09:14"),Status="Verificada",Payment="PSE",Recipient="Comercio Andino",Device="Conocido",Location="Bogotá",Notes="Operación habitual",Risk="Bajo",Factors="Dispositivo y ubicación habituales"},
            new(){Id="HST-2025-002",User="Carlos Ruiz",Amount=8750000,Date=DateTime.Parse("2025-05-22T10:42"),Status="Con inconsistencias",Payment="Transferencia",Recipient="Servicios Globales",Device="Nuevo",Location="Ubicación inusual",Notes="Monto distinto al registro de referencia",Risk="Alto",Factors="Monto superior a $5.000.000, Dispositivo nuevo, Ubicación inusual, Datos inconsistentes"},
            new(){Id="HST-2025-003",User="Ana Torres",Amount=980000,Date=DateTime.Parse("2025-05-22T11:20"),Status="Pendiente",Payment="Tarjeta débito",Recipient="Mercado Central",Device="Conocido",Location="Medellín",Notes="Pendiente de revisión",Risk="Medio",Factors="Transacción pendiente de verificación"},
            new(){Id="HST-2025-004",User="Diego León",Amount=6200000,Date=DateTime.Parse("2025-05-21T16:08"),Status="Verificada",Payment="PSE",Recipient="Inversiones Norte",Device="Conocido",Location="Cali",Notes="Monto alto validado",Risk="Medio",Factors="Monto superior a $5.000.000"},
            new(){Id="HST-2025-005",User="Sofía Méndez",Amount=125000,Date=DateTime.Parse("2025-05-21T14:35"),Status="Verificada",Payment="Billetera digital",Recipient="Librería Nacional",Device="Conocido",Location="Bogotá",Notes="Compra académica",Risk="Bajo",Factors="Sin factores de riesgo relevantes"}
        ],
        Alerts = [new(){Id="ALT-001",TxId="HST-2025-002",Level="Alto",Reason="Monto alto, dispositivo nuevo, ubicación inusual e inconsistencias",Date=DateTime.Parse("2025-05-22T10:43"),Status="Activa"}]
    };

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public async Task InitializeAsync()
    {
        await gate.WaitAsync();
        try
        {
            await using var connection = OpenConnection();
            var schema = connection.CreateCommand();
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS Transactions(
                    Id TEXT PRIMARY KEY, UserName TEXT NOT NULL, Amount INTEGER NOT NULL,
                    Date TEXT NOT NULL, Status TEXT NOT NULL, Payment TEXT NOT NULL,
                    Recipient TEXT NOT NULL, Device TEXT NOT NULL, Location TEXT NOT NULL,
                    Notes TEXT NOT NULL, Risk TEXT NOT NULL, Factors TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS Alerts(
                    Id TEXT PRIMARY KEY, TxId TEXT NOT NULL UNIQUE,
                    Level TEXT NOT NULL, Reason TEXT NOT NULL, Date TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    FOREIGN KEY(TxId) REFERENCES Transactions(Id) ON DELETE CASCADE);
                """;
            await schema.ExecuteNonQueryAsync();

            var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM Transactions;";
            if (Convert.ToInt64(await count.ExecuteScalarAsync()) == 0)
            {
                var initial = await ReadLegacyOrSeedAsync();
                await InsertDataAsync(connection, initial);
            }
        }
        finally { gate.Release(); }
    }

    private static async Task InsertDataAsync(SqliteConnection connection, LegacyStore data, SqliteTransaction? transaction = null)
    {
        foreach (var t in data.Transactions)
        {
            var cmd = connection.CreateCommand(); cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO Transactions VALUES ($id,$user,$amount,$date,$status,$payment,$recipient,$device,$location,$notes,$risk,$factors);";
            AddTransactionParameters(cmd, t);
            await cmd.ExecuteNonQueryAsync();
        }
        foreach (var a in data.Alerts)
        {
            var cmd = connection.CreateCommand(); cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO Alerts VALUES ($id,$tx,$level,$reason,$date,$status);";
            cmd.Parameters.AddWithValue("$id", a.Id); cmd.Parameters.AddWithValue("$tx", a.TxId);
            cmd.Parameters.AddWithValue("$level", a.Level); cmd.Parameters.AddWithValue("$reason", a.Reason);
            cmd.Parameters.AddWithValue("$date", a.Date.ToString("O")); cmd.Parameters.AddWithValue("$status", a.Status);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<LegacyStore> ReadLegacyOrSeedAsync()
    {
        if (!File.Exists(JsonPath)) return Seed();
        try
        {
            await using var file = File.OpenRead(JsonPath);
            return await JsonSerializer.DeserializeAsync<LegacyStore>(file, JsonOptions) ?? Seed();
        }
        catch (JsonException) { return Seed(); }
    }

    private static void AddTransactionParameters(SqliteCommand cmd, TransactionRecord t)
    {
        cmd.Parameters.AddWithValue("$id", t.Id); cmd.Parameters.AddWithValue("$user", t.User);
        cmd.Parameters.AddWithValue("$amount", t.Amount); cmd.Parameters.AddWithValue("$date", t.Date.ToString("O"));
        cmd.Parameters.AddWithValue("$status", t.Status); cmd.Parameters.AddWithValue("$payment", t.Payment);
        cmd.Parameters.AddWithValue("$recipient", t.Recipient); cmd.Parameters.AddWithValue("$device", t.Device);
        cmd.Parameters.AddWithValue("$location", t.Location); cmd.Parameters.AddWithValue("$notes", t.Notes);
        cmd.Parameters.AddWithValue("$risk", t.Risk); cmd.Parameters.AddWithValue("$factors", t.Factors);
    }

    public async Task<List<TransactionRecord>> TransactionsAsync()
    {
        await gate.WaitAsync();
        try
        {
            await using var c = OpenConnection(); var q = c.CreateCommand(); q.CommandText = "SELECT * FROM Transactions ORDER BY Date DESC;";
            await using var r = await q.ExecuteReaderAsync(); var rows = new List<TransactionRecord>();
            while (await r.ReadAsync()) rows.Add(new(){Id=r.GetString(0),User=r.GetString(1),Amount=r.GetInt64(2),Date=DateTime.Parse(r.GetString(3)),Status=r.GetString(4),Payment=r.GetString(5),Recipient=r.GetString(6),Device=r.GetString(7),Location=r.GetString(8),Notes=r.GetString(9),Risk=r.GetString(10),Factors=r.GetString(11)});
            return rows;
        }
        finally { gate.Release(); }
    }

    public async Task<List<AlertRecord>> AlertsAsync()
    {
        await gate.WaitAsync();
        try
        {
            await using var c = OpenConnection(); var q = c.CreateCommand(); q.CommandText = "SELECT * FROM Alerts ORDER BY Date DESC;";
            await using var r = await q.ExecuteReaderAsync(); var rows = new List<AlertRecord>();
            while (await r.ReadAsync()) rows.Add(new(){Id=r.GetString(0),TxId=r.GetString(1),Level=r.GetString(2),Reason=r.GetString(3),Date=DateTime.Parse(r.GetString(4)),Status=r.GetString(5)});
            return rows;
        }
        finally { gate.Release(); }
    }

    public async Task AddAsync(TransactionRecord t, bool createAlert)
    {
        await gate.WaitAsync();
        try
        {
            await using var c = OpenConnection(); await using var tx = (SqliteTransaction)await c.BeginTransactionAsync();
            var insert = c.CreateCommand(); insert.Transaction = tx;
            insert.CommandText = "INSERT INTO Transactions VALUES ($id,$user,$amount,$date,$status,$payment,$recipient,$device,$location,$notes,$risk,$factors);";
            AddTransactionParameters(insert, t); await insert.ExecuteNonQueryAsync();
            if (createAlert)
            {
                var alert = c.CreateCommand(); alert.Transaction = tx;
                alert.CommandText = "INSERT INTO Alerts VALUES ($id,$tx,'Alto',$reason,$date,'Activa');";
                alert.Parameters.AddWithValue("$id", "ALT-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                alert.Parameters.AddWithValue("$tx", t.Id); alert.Parameters.AddWithValue("$reason", t.Factors);
                alert.Parameters.AddWithValue("$date", DateTime.Now.ToString("O")); await alert.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        finally { gate.Release(); }
    }

    public async Task SetAlertStatusAsync(string id, string status)
    {
        await gate.WaitAsync();
        try { await using var c=OpenConnection(); var q=c.CreateCommand(); q.CommandText="UPDATE Alerts SET Status=$status WHERE Id=$id;"; q.Parameters.AddWithValue("$status",status); q.Parameters.AddWithValue("$id",id); await q.ExecuteNonQueryAsync(); }
        finally { gate.Release(); }
    }

    public async Task RestoreAsync()
    {
        await gate.WaitAsync();
        try
        {
            await using var c=OpenConnection(); await using var tx=(SqliteTransaction)await c.BeginTransactionAsync();
            var clear=c.CreateCommand(); clear.Transaction=tx; clear.CommandText="DELETE FROM Alerts; DELETE FROM Transactions;"; await clear.ExecuteNonQueryAsync();
            await InsertDataAsync(c,Seed(),tx); await tx.CommitAsync();
        }
        finally { gate.Release(); }
    }
}
