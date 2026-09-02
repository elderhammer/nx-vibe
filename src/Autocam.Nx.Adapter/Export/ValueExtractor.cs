using System;
using System.Collections.Generic;
using System.Reflection;
using NXOpen.CAM;

namespace Autocam.Nx.Adapter.Export
{
    /// <summary>
    /// 值提取器：Builder 属性链叶子 → plan 口径值（mm/rpm/枚举串/复合对象）。
    /// 反射通用路径：Inheritable*Builder 取 Value、枚举取名、bool 直取、表达式取 Value。
    /// </summary>
    public static class ValueExtractor
    {
        public static object Extract(object leaf)
        {
            if (leaf == null)
            {
                return null;
            }
            var type = leaf.GetType();
            if (type == typeof(InheritableFeedBuilder))
            {
                // 进给：plan 口径复合对象 {value}（单位字段 NX 侧未定位，缺项不伪造——schema feed.unit 可选）
                var v = ReadProperty(leaf, "Value");
                if (v == null)
                {
                    return null;
                }
                return new Dictionary<string, object> { { "value", v } };
            }
            if (type == typeof(InheritableDoubleBuilder) || type == typeof(InheritableIntBuilder)
                || type == typeof(InheritableToolDepBuilder))
            {
                return ReadProperty(leaf, "Value");
            }
            if (type.Name == "ExpressionDouble")
            {
                return ReadProperty(leaf, "Value");
            }
            if (leaf is bool)
            {
                return leaf;
            }
            if (type.IsEnum)
            {
                return ToUpperSnake(Enum.GetName(type, leaf));   // 枚举 → plan 口径（大写蛇形，schema pattern）
            }
            // 数值（CutParameters 层可能有裸 double）
            if (leaf is double || leaf is int || leaf is long)
            {
                return leaf;
            }
            // Stepover 复合：{mode, value}（value 读不到 → 整体缺项，不产 null 值违反 schema）
            if (type.Name.IndexOf("Stepover", StringComparison.Ordinal) >= 0)
            {
                var mode = SafeRead(leaf, "Type");
                var percent = SafeRead(leaf, "Percent");
                var value = SafeRead(leaf, "Value");
                var result = new Dictionary<string, object>();
                if (mode != null && mode.GetType().IsEnum)
                {
                    result["mode"] = ToUpperSnake(Enum.GetName(mode.GetType(), mode));
                }
                if (percent != null && percent.GetType().IsEnum)
                {
                    result["mode"] = ToUpperSnake(Enum.GetName(percent.GetType(), percent));
                }
                var v = value ?? percent;
                if (v == null)
                {
                    return null;
                }
                result["value"] = v;
                return result;
            }
            // 未知叶子：返回 null + 调用侧缺项（绝不静默，不伪造值）
            return null;
        }

        /// <summary>PascalCase 枚举名 → 大写蛇形（LevelFirst → LEVEL_FIRST，plan schema 口径）。</summary>
        public static string ToUpperSnake(string pascal)
        {
            if (string.IsNullOrEmpty(pascal))
            {
                return pascal;
            }
            var sb = new System.Text.StringBuilder();
            foreach (var c in pascal)
            {
                if (char.IsUpper(c) && sb.Length > 0)
                {
                    sb.Append('_');
                }
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>沿点分路径反射读属性链（任一段缺失 → null）。</summary>
        public static object ReadPath(object root, string path)
        {
            object current = root;
            foreach (var segment in path.Split('.'))
            {
                if (current == null)
                {
                    return null;
                }
                var prop = current.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null)
                {
                    return null;
                }
                try
                {
                    current = prop.GetValue(current, null);
                }
                catch (TargetInvocationException)
                {
                    return null;   // NX 属性访问在批处理下的兼容性差异（如 BuilderProperties）→ 缺项
                }
            }
            return current;
        }

        private static object ReadProperty(object target, string name)
        {
            var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
            {
                return null;
            }
            try
            {
                return prop.GetValue(target, null);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        private static object SafeRead(object target, string name)
        {
            return ReadProperty(target, name);
        }
    }
}
