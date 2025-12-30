using System.Reflection;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine.EventSystems;

namespace QuickPrice.Patches
{
    /// <summary>
    /// 捕获鼠标进入物品事件
    /// </summary>
    public class GridItemOnPointerEnterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.FirstMethod(
                typeof(GridItemView),
                x => x.Name == nameof(GridItemView.OnPointerEnter)
            );
        }

        [PatchPrefix]
        public static void Prefix(GridItemView __instance, PointerEventData eventData)
        {
            Plugin.HoveredItem = __instance?.Item;

            try
            {
                // 如果当前 GridItemView 来自商人/售卖界面（类型名常见为 GoodsItemView 等），尝试标记
                var viewTypeName = __instance.GetType().Name;
                if (viewTypeName.Contains("Goods") || viewTypeName.Contains("Trader") || viewTypeName.Contains("Offer"))
                {
                    Plugin.HoveredItemFromTrader = true;
                }
                else
                {
                    Plugin.HoveredItemFromTrader = false;
                }
            }
            catch
            {
                Plugin.HoveredItemFromTrader = false;
            }
        }
    }

    /// <summary>
    /// 捕获鼠标离开物品事件
    /// </summary>
    public class GridItemOnPointerExitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.FirstMethod(
                typeof(GridItemView),
                x => x.Name == nameof(GridItemView.OnPointerExit)
            );
        }

        [PatchPrefix]
        public static void Prefix(GridItemView __instance, PointerEventData eventData)
        {
            Plugin.HoveredItem = null;
            Plugin.HoveredItemFromTrader = false;
        }
    }
}
