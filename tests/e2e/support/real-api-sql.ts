import { execFileSync } from 'node:child_process';

// Read-back helpers for the real-API specs: every screen value is checked against the row SQL Server saved.
const sqlServer = process.env['E2E_SQL_SERVER'] ?? 'localhost\\MSSQLSERVER2022';
const sqlDatabase = process.env['E2E_SQL_DATABASE'] ?? 'myerp';

/**
 * Runs a read-only query with Windows authentication and returns its rows. The query must select only ASCII values
 * (sqlcmd writes the console code page), so names are selected as UTF-16 hex and decoded with {@link utf16}.
 */
export function sql<T>(query: string): T[] {
  const output = execFileSync('sqlcmd', ['-S', sqlServer, '-d', sqlDatabase, '-E', '-C', '-b', '-y', '0',
    '-Q', `SET NOCOUNT ON; SELECT (${query} FOR JSON PATH, INCLUDE_NULL_VALUES) AS rows_json`], { encoding: 'utf8' });
  const text = output.replace(/\r?\n/g, '').trim();
  return text && text !== 'NULL' ? JSON.parse(text) as T[] : [];
}

/** Decodes a value selected as `CONVERT(varchar(max), CONVERT(varbinary(max), nvarchar_column), 2)`. */
export const utf16 = (hex: string) => Buffer.from(hex, 'hex').toString('utf16le');

export const companyId = (code: string) => sql<{ id: number }>(`SELECT id FROM sys_company WHERE code = '${code}'`)[0]!.id;

/**
 * Arranges test-only source rows that no HTTP API can create yet (ADR-204: PUR purchase orders and receipts arrive with
 * 006/007). Runs `statements` in one transaction (XACT_ABORT) and returns the rows of `select`; ASCII values only.
 */
export function sqlArrange<T>(statements: string, select: string): T[] {
  const output = execFileSync('sqlcmd', ['-S', sqlServer, '-d', sqlDatabase, '-E', '-C', '-b', '-y', '0',
    '-Q', `SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRAN; ${statements}; COMMIT;
      SELECT (${select} FOR JSON PATH, INCLUDE_NULL_VALUES) AS rows_json`], { encoding: 'utf8' });
  const text = output.replace(/\r?\n/g, '').trim();
  return text && text !== 'NULL' ? JSON.parse(text) as T[] : [];
}
