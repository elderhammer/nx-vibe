using System;
using System.Collections.Generic;
using Autocam.Plan.Core.Dto;
using Autocam.Plan.Core.Diagnostics;

namespace Autocam.PlanExporter.Core.Export
{
    /// <summary>
    /// §4.5：几何锚点提取（FaceResolver 反向，唯一需要匹配算法的环节）。
    /// Core 不做 NX API 计算（那是适配层）——这里负责：
    /// 1) 工序关联几何 Tag → Face/Edge 快照匹配（在 Flatten 阶段完成，此处消费）；
    /// 2) 特征 anchor_point = 首张关联面的质心（快照顺序须确定，见 DTO 注释）；
    /// 3) 对称碰撞检测：两张面属性元组（质心+面积+类型+法向）在 0.01mm 容差内相同
    ///    → warning 提示人工复核（nx-plugin-design.md §6 几何映射风险）。
    /// </summary>
    public sealed class AnchorPackager
    {
        /// <summary>容差（mm），对齐 I5（§3.4）。</summary>
        public const double AnchorTolerance = 0.01;

        private const double NormalDotTolerance = 0.9999;

        private readonly DiagnosticsCollector _diag;

        public AnchorPackager(DiagnosticsCollector diag)
        {
            _diag = diag;
        }

        /// <summary>返回 工序 → anchor_point（无可用面时缺项，调用侧省略 geometry_ref）。</summary>
        public Dictionary<OperationSnapshot, double[]> Analyze(IList<OpResolved> ops)
        {
            var anchors = new Dictionary<OperationSnapshot, double[]>();

            foreach (var resolved in ops)
            {
                if (resolved.Faces.Count > 0)
                {
                    anchors[resolved.Op] = resolved.Faces[0].Centroid;
                }
            }

            // 对称碰撞检测：工序关联面两两比较（O(n²)，MVP 面规模下可接受）
            var faces = new List<FaceSnapshot>();
            foreach (var resolved in ops)
            {
                faces.AddRange(resolved.Faces);
            }
            for (var i = 0; i < faces.Count; i++)
            {
                for (var j = i + 1; j < faces.Count; j++)
                {
                    if (Collides(faces[i], faces[j]))
                    {
                        _diag.Warning("ANCHOR_COLLISION",
                            string.Format("面 {0} 与面 {1} 属性元组在 {2}mm 容差内相同（对称/阵列面疑似），请人工复核几何匹配（nx-plugin-design.md §6）",
                                faces[i].Tag, faces[j].Tag, AnchorTolerance));
                    }
                }
            }

            return anchors;
        }

        /// <summary>属性元组碰撞判定：类型相同、面积差 ≤ 容差、质心距 ≤ 容差、法向夹角近零。</summary>
        private static bool Collides(FaceSnapshot a, FaceSnapshot b)
        {
            if (!string.Equals(a.FaceType, b.FaceType, StringComparison.Ordinal))
            {
                return false;
            }
            if (Math.Abs(a.Area - b.Area) > AnchorTolerance)
            {
                return false;
            }
            if (Distance(a.Centroid, b.Centroid) > AnchorTolerance)
            {
                return false;
            }
            if (a.Normal == null || b.Normal == null)
            {
                return true;
            }
            return Dot(a.Normal, b.Normal) >= NormalDotTolerance;
        }

        private static double Distance(double[] p, double[] q)
        {
            var dx = p[0] - q[0];
            var dy = p[1] - q[1];
            var dz = p[2] - q[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static double Dot(double[] u, double[] v)
        {
            return u[0] * v[0] + u[1] * v[1] + u[2] * v[2];
        }
    }
}
