namespace Scarab.ResultBackends;

internal class InsertSchema
{
    public string DropTable { get; set; } = "";
    public string CreateTable { get; set; } = "";
    public string InsertRow { get; set; } = "";
}
