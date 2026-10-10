-- 範例 / 冒煙用 SP：回傳目前資料庫名稱與定序，供部署驗證與診斷（WU-P0-008）。
-- 依 db-conventions.md 第 7 節：CREATE OR ALTER、SET NOCOUNT ON、必有 @company_id 參數。
CREATE OR ALTER PROCEDURE dbo.usp_sys_database_info
    @company_id BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        @company_id                                                       AS company_id,
        CAST(DB_NAME() AS NVARCHAR(128))                                  AS database_name,
        CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS NVARCHAR(128)) AS collation_name;
END
