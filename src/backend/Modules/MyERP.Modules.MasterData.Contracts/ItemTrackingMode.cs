using System.Text.Json.Serialization;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>
/// 物料的庫存追蹤方式（ADR-060）：None 只按數量；Lot 入庫須指定批號，出庫須指定可用批號；
/// Serial 每個基本單位一個公司內唯一序號。物料已有庫存、交易或單據後不可修改。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ItemTrackingMode>))]
public enum ItemTrackingMode
{
    None,
    Lot,
    Serial,
}
