using System.Data.Common;

namespace Scarab.ResultBackends;

public interface IResultSet : IAsyncDisposable
{
    Task RegisterColTypes(string[] columnNames, DbColumn[] columnTypes);
    bool IsColTypesRegistered();
    Task WriteCols(string[] columnNames);
    Task WriteRow(object[] values);
    Task Flush();
}
