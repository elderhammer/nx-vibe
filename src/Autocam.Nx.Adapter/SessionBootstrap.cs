using System;
using NXOpen;
using NXOpen.CAM;

namespace Autocam.Nx.Adapter
{
    /// <summary>
    /// 批处理会话引导（M0 实测链路，缺一步都失败——nx-journal-manual-verification.md M0 节）：
    /// NewDisplay → SetWork → CreateCamSession → CreateCamSetup。
    /// 打开已有零件用 OpenDisplay + SetWork（批处理下 Open 不建显示）。
    /// </summary>
    public static class SessionBootstrap
    {
        public const string CamSetupTemplate = "mill_planar";

        /// <summary>新建零件 + CAMSetup（重建侧入口，D-适配-3 另存副本载体）。</summary>
        public static CAMSetup NewPartWithCamSetup(Session session, string partName)
        {
            if (session == null)
            {
                throw new ArgumentNullException("session");
            }
            var part = session.Parts.NewDisplay(partName, Part.Units.Millimeters);
            session.Parts.SetWork(part);
            EnsureCamSession(session);
            var camSetup = part.CreateCamSetup(CamSetupTemplate);
            if (camSetup == null)
            {
                throw new InvalidOperationException("CreateCamSetup 返回 null（模板 " + CamSetupTemplate + " 不可用）");
            }
            return camSetup;
        }

        /// <summary>打开已有零件并设 work part（导出侧入口）。</summary>
        public static CAMSetup OpenPart(Session session, string partPath)
        {
            if (session == null)
            {
                throw new ArgumentNullException("session");
            }
            PartLoadStatus loadStatus;
            var part = session.Parts.OpenDisplay(partPath, out loadStatus);
            session.Parts.SetWork(part);
            EnsureCamSession(session);
            return part.CAMSetup;
        }

        public static void EnsureCamSession(Session session)
        {
            if (!session.IsCamSessionInitialized())
            {
                session.CreateCamSession();
            }
        }
    }
}
