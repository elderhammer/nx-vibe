using System.Collections.Generic;
using Autocam.Plan.Core.Plan;

namespace Autocam.Plan.Core.Diagnostics
{
    /// <summary>
    /// 诊断唯一汇聚点：所有阶段的缺项/异常/碰撞都必须经此落 diagnostics[]，
    /// "绝不静默省略"（plan-exporter.md §3.3-2）靠单一汇聚点才可测。
    /// </summary>
    public sealed class DiagnosticsCollector
    {
        public const string LevelInfo = "INFO";
        public const string LevelWarning = "WARNING";
        public const string LevelError = "ERROR";

        private readonly List<DiagnosticEntry> _entries = new List<DiagnosticEntry>();

        public IReadOnlyList<DiagnosticEntry> Entries => _entries;

        public void Add(string level, string code, string detail)
        {
            _entries.Add(new DiagnosticEntry { Level = level, Code = code, Detail = detail });
        }

        public void Info(string code, string detail) => Add(LevelInfo, code, detail);
        public void Warning(string code, string detail) => Add(LevelWarning, code, detail);
        public void Error(string code, string detail) => Add(LevelError, code, detail);

        /// <summary>是否有 ERROR 级条目（供测试快速断言"无错误"）。</summary>
        public bool HasErrors
        {
            get { return _entries.Exists(e => e.Level == LevelError); }
        }
    }
}
