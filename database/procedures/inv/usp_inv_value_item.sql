CREATE OR ALTER PROCEDURE dbo.usp_inv_value_item
    @company_id bigint,
    @item_id bigint,
    @cutoff_posted_at datetime2(6) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @company_id <= 0 OR @item_id <= 0
        THROW 51030, 'INV.VALUATION.INVALID_SCOPE', 1;

    -- seq is the (posted_at, id) valuation order, so the cursor reads the clustered key without a sort worktable.
    CREATE TABLE #events (
        seq bigint NOT NULL PRIMARY KEY CLUSTERED,
        id bigint NOT NULL,
        posted_at datetime2(6) NOT NULL,
        direction nvarchar(10) NOT NULL,
        quantity decimal(19,6) NOT NULL,
        unit_cost decimal(19,6) NULL,
        source_type varchar(30) NOT NULL,
        source_id bigint NOT NULL,
        source_line_no int NOT NULL
    );

    -- HOLDLOCK fixes the ledger range during this read without changing a caller's
    -- transaction isolation level. The transaction is released before calculation.
    BEGIN TRANSACTION;
    INSERT INTO #events (seq, id, posted_at, direction, quantity, unit_cost, source_type, source_id, source_line_no)
    SELECT ROW_NUMBER() OVER (ORDER BY posted_at, id), id, posted_at, direction, quantity, unit_cost, source_type, source_id, source_line_no
    FROM dbo.inv_stock_movement WITH (INDEX(ix_inv_stock_movement_valuation), HOLDLOCK)
    WHERE company_id = @company_id AND item_id = @item_id
      AND (@cutoff_posted_at IS NULL OR posted_at <= @cutoff_posted_at);
    COMMIT TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM #events WHERE source_type = 'TRANSFER'
        GROUP BY source_id, source_line_no
        HAVING COUNT_BIG(*) <> 2
           OR SUM(CASE WHEN direction = 'In' THEN 1 ELSE 0 END) <> 1
           OR SUM(CASE WHEN direction = 'Out' THEN 1 ELSE 0 END) <> 1
           OR SUM(CASE WHEN direction = 'In' THEN quantity ELSE -quantity END) <> 0
           OR SUM(CASE WHEN unit_cost IS NULL THEN 0 ELSE 1 END) <> 0
    ) THROW 51031, 'INV.VALUATION.TRANSFER_UNPAIRED', 1;

    DECLARE @quantity decimal(38,6) = 0,
            @value decimal(38,12) = 0,
            @unknown bit = 0,
            @movement_count bigint = 0,
            @id bigint, @direction nvarchar(10), @line_quantity decimal(19,6),
            @unit_cost decimal(19,6), @source_type varchar(30),
            @line_value decimal(38,12);

    DECLARE movement_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT id, direction, quantity, unit_cost, source_type
        FROM #events ORDER BY seq;
    OPEN movement_cursor;
    FETCH NEXT FROM movement_cursor INTO @id, @direction, @line_quantity, @unit_cost, @source_type;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @movement_count += 1;
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
        FETCH NEXT FROM movement_cursor INTO @id, @direction, @line_quantity, @unit_cost, @source_type;
    END;
    CLOSE movement_cursor;
    DEALLOCATE movement_cursor;

    IF @quantity > 9999999999999.999999 OR (@unknown = 0 AND @value > 999999999999999.9999)
        THROW 51033, 'INV.VALUATION.OVERFLOW', 1;

    SELECT CONVERT(decimal(19,6), @quantity) AS on_hand,
           CASE WHEN @unknown = 1 THEN CONVERT(decimal(19,4), NULL) ELSE CONVERT(decimal(19,4), @value) END AS value,
           CASE WHEN @unknown = 1 OR @quantity = 0 THEN CONVERT(decimal(19,6), NULL)
                ELSE CONVERT(decimal(19,6), ROUND(@value / @quantity, 6)) END AS average_unit_cost,
           CASE WHEN @unknown = 1 THEN CONVERT(decimal(19,6), @quantity) ELSE CONVERT(decimal(19,6), 0) END AS unpriced_quantity,
           CASE WHEN @unknown = 1 THEN 'Unpriced' ELSE 'Valued' END AS status,
           @movement_count AS movement_count;
END;
