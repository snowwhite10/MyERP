using MyERP.Shared.Domain;

namespace MyERP.Modules.System.Domain;

/// <summary>
/// 公司層級的系統參數值（sys_parameter，REQ_DS43.73；BlueWhale SystemSet）。只保存修改過的參數，
/// 沒有資料列時使用模組宣告的預設值；值為依參數型別正規化後的字串（ADR-026）。
/// </summary>
public sealed class SystemParameter : BusinessEntity
{
    public required string ParamKey { get; set; }

    public required string ParamValue { get; set; }
}
