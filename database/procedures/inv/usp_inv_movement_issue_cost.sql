CREATE OR ALTER PROCEDURE dbo.usp_inv_movement_issue_cost
    @company_id bigint,
    @movement_id bigint
AS
BEGIN
    -- Unit cost at which one posted outbound movement left the moving-average pool (ADR-188):
    -- the same replay as usp_inv_value_item, but stopping strictly before the target in
    -- (posted_at, id) order, so movements sharing the target's timestamp are still ordered exactly.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @company_id <= 0 OR @movement_id <= 0
        THROW 51030, 'INV.VALUATION.INVALID_SCOPE', 1;

    DECLARE @item_id bigint, @target_posted_at datetime2(6), @target_direction nvarchar(10);
    SELECT @item_id = item_id, @target_posted_at = posted_at, @target_direction = direction
    FROM dbo.inv_stock_movement
    WHERE company_id = @company_id AND id = @movement_id;

    IF @item_id IS NULL OR @target_direction <> 'Out'
        THROW 51038, 'INV.VALUATION.MOVEMENT_NOT_ISSUE', 1;

    CREATE TABLE #events (
        seq bigint NOT NULL PRIMARY KEY CLUSTERED,
        direction nvarchar(10) NOT NULL,
        quantity decimal(19,6) NOT NULL,
        unit_cost decimal(19,6) NULL,
        source_type varchar(30) NOT NULL
    );

    -- History before an existing movement is append-only; HOLDLOCK keeps the read range stable
    -- for the caller's transaction exactly like usp_inv_value_item.
    BEGIN TRANSACTION;
    INSERT INTO #events (seq, direction, quantity, unit_cost, source_type)
    SELECT ROW_NUMBER() OVER (ORDER BY posted_at, id), direction, quantity, unit_cost, source_type
    FROM dbo.inv_stock_movement WITH (INDEX(ix_inv_stock_movement_valuation), HOLDLOCK)
    WHERE company_id = @company_id AND item_id = @item_id
      AND (posted_at < @target_posted_at OR (posted_at = @target_posted_at AND id < @movement_id));
    COMMIT TRANSACTION;

    DECLARE @quantity decimal(38,6) = 0,
            @value decimal(38,12) = 0,
            @unknown bit = 0,
            @direction nvarchar(10), @line_quantity decimal(19,6),
            @unit_cost decimal(19,6), @source_type varchar(30),
            @line_value decimal(38,12);

    DECLARE movement_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT direction, quantity, unit_cost, source_type FROM #events ORDER BY seq;
    OPEN movement_cursor;
    FETCH NEXT FROM movement_cursor INTO @direction, @line_quantity, @unit_cost, @source_type;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @source_type <> 'TRANSFER'
        BEGIN
            IF @direction = 'In'
            BEGIN
                IF @unit_cost IS NOT NULL
                    SET @line_value = ROUND(CONVERT(decimal(38,12), @line_quantity) * @unit_cost, 4);
                ELSE IF @quantity > 0 AND @unknown = 0
                    SET @line_value = ROUND(CONVERT(decimal(38,12), @line_quantity) * @value / @quantity, 4);
                ELSE
                BEGIN
                    SET @unknown = 1;
                    SET @line_value = 0;
                END;
                SET @quantity += @line_quantity;
                IF @unknown = 0 SET @value += @line_value;
            END
            ELSE
            BEGIN
                IF @line_quantity > @quantity
                    THROW 51032, 'INV.VALUATION.NEGATIVE_POOL', 1;
                IF @line_quantity = @quantity
                BEGIN
                    SET @quantity = 0;
                    SET @value = 0;
                    SET @unknown = 0;
                END
                ELSE
                BEGIN
                    IF @unknown = 0
                    BEGIN
                        SET @line_value = ROUND(CONVERT(decimal(38,12), @line_quantity) * @value / @quantity, 4);
                        SET @value -= @line_value;
                    END;
                    SET @quantity -= @line_quantity;
                END
            END
        END;
        FETCH NEXT FROM movement_cursor INTO @direction, @line_quantity, @unit_cost, @source_type;
    END;
    CLOSE movement_cursor;
    DEALLOCATE movement_cursor;

    IF @quantity > 9999999999999.999999 OR (@unknown = 0 AND @value > 999999999999999.9999)
        THROW 51033, 'INV.VALUATION.OVERFLOW', 1;

    -- NULL when the pool before the issue was unpriced or empty: the issue had no known cost.
    SELECT CASE WHEN @unknown = 1 OR @quantity = 0 THEN CONVERT(decimal(19,6), NULL)
                ELSE CONVERT(decimal(19,6), ROUND(@value / @quantity, 6)) END AS unit_cost;
END;
