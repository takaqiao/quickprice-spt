using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace QuickPrice.Patches
{
    /// <summary>
    /// 当鼠标移入任意 UI 可交互控件（Selectable）时清除 HoveredItem，防止之前的物品价格在按钮上持续显示
    /// 这将阻止“上一个物品查询完后，鼠标移到任意UI按钮仍显示价格”的问题。
    /// </summary>
    public class ClearHoveredOnUIEnterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // 目标：UnityEngine.UI.Selectable.OnPointerEnter(PointerEventData eventData)
            return AccessTools.FirstMethod(
                typeof(Selectable),
                m => m.Name == "OnPointerEnter" && m.GetParameters().Length == 1
            );
        }

        [PatchPrefix]
        public static void Prefix(Selectable __instance, PointerEventData eventData)
        {
            try
            {
                // 如果是 GridItemView（物品格子），不要清除（GridItemView 有自己的补丁管理 HoveredItem）
                var typeName = __instance.GetType().Name;
                if (typeName.Contains("GridItem") || typeName.Contains("GridItemView"))
                    return;

                // 清除 HoveredItem，避免旧物品价格出现在按钮上
                QuickPrice.Plugin.HoveredItem = null;
                QuickPrice.Plugin.HoveredItemFromTrader = false;
            }
            catch
            {
                // 忽略错误
            }
        }
    }
}
