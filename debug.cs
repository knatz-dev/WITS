using Microsoft.Data.Sqlite;
using WITS.Migrator.Migrations;
var path = Path.Combine(Path.GetTempPath(), "opencode", "debug.db");
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
foreach (var suffix in new[]{"""",""-wal"",""-shm"",""-journal""}) { try { File.Delete(path+suffix);}catch{} }
var cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
new Migration20250419113604SystemSchema().Apply(cs);
Console.WriteLine("count before insert: " + new SqliteConnection(cs).ExecuteScalar<int>("SELECT COUNT(*) FROM Organisation"));
using var c = new SqliteConnection(cs);
c.Open();
c.Execute("INSERT INTO Organisation (Code, Name, Created) VALUES ('REG','Registration org','2026-11-08T00:00:00Z');");
Console.WriteLine("inserted, count: " + c.ExecuteScalar<int>("SELECT COUNT(*) FROM Organisation"));
