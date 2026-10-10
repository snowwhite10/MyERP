using MyERP.DocGen.Schema;
using MyERP.DocGen.Sources;
using MyERP.DocGen.Word;

namespace MyERP.DocGen.Generation;

/// <summary>一次產生所需的全部輸入。</summary>
public sealed record GenerationContext(RepositoryLayout Layout, RequirementSet Requirements, DatabaseSchema Schema, DateOnly Date);

/// <summary>一份 Word 文件的產生結果：標題大綱（一致性檢查用）與缺少的來源檔。</summary>
public sealed record DocumentResult(string Path, IReadOnlyList<OutlineEntry> Outline, IReadOnlyList<string> MissingSources);
