using System.Data;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// WU-FI-004a3x1b1: whether a posting failure left the connection unusable, so the commit result may be unknown
/// and <see cref="Application.GlPostingFailureRules"/> must resolve it from the committed source key.
/// </summary>
public static class GlPostingConnectionRules
{
    /// <summary>SQL Server closes the connection for errors of severity 20 and above.</summary>
    public const byte FatalSeverity = 20;

    public static bool IsConnectionLost(Exception? exception, ConnectionState stateAfterFailure)
    {
        if (stateAfterFailure is ConnectionState.Broken or ConnectionState.Closed)
            return true;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException or SocketException
                || current is SqlException sql && sql.Class >= FatalSeverity)
                return true;
        }
        return false;
    }
}
