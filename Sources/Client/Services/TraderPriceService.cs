using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using QuickPrice.Models;
using QuickPrice.Extensions;

namespace QuickPrice.Services
{
    /// <summary>
    /// 商人价格服务
    /// 负责获取商人收购价格
    /// v2.1: 优化容器克隆性能，添加价格缓存
    /// </summary>
    public class TraderPriceService
    {
        private static TraderPriceService _instance;
        public static TraderPriceService Instance => _instance ??= new TraderPriceService();

        private bool _hasShownInitTip = false;  // 是否已显示初始化提示

        // ===== 性能优化：缓存系统 =====

        /// <summary>
        /// 商人价格缓存（按物品TemplateId缓存）
        /// 因为同一物品的商人价格是固定的，无需重复计算
        /// </summary>
        private Dictionary<string, TraderPrice> _priceCache = new Dictionary<string, TraderPrice>();

        /// <summary>
        /// 反射属性缓存（避免重复反射）
        /// Key: 物品类型 (Type), Value: IsContainer 属性信息
        /// </summary>
        private static Dictionary<Type, PropertyInfo> _containerPropertyCache = new Dictionary<Type, PropertyInfo>();

        // 最大合理价格阈值（卢布） — 超出则视为异常/卖家列表，忽略
        private const double MAX_REASONABLE_TRADER_PRICE_ROUBLES = 100_000_000d;

        private TraderPriceService() { }

        /// <summary>
        /// 获取最佳商人收购价格
        /// </summary>
        /// <param name="item">物品</param>
        /// <returns>最高收购价格，如果没有商人收购则返回 null</returns>
        public TraderPrice GetBestTraderPrice(Item item)
        {
            try
            {
                if (item == null)
                    return null;

                // ===== 优化1: 先查缓存 =====
                string cacheKey = item.TemplateId;
                if (_priceCache.TryGetValue(cacheKey, out var cachedPrice))
                {
                    return cachedPrice;
                }

                TraderPrice highestPrice = null;

                // 获取所有商人
                var traders = GetAllTraders();
                if (traders == null || !traders.Any())
                {
                    if (!_hasShownInitTip)
                    {
                        _hasShownInitTip = true;
                    }
                    return null;
                }

                // 遍历所有商人
                foreach (TraderClass trader in traders)
                {
                    // 检查商人是否可用
                    if (!IsTraderAvailable(trader))
                        continue;

                    try
                    {
                        Item itemToPrice;

                        // ===== 优化2: 容器检测，避免深拷贝 =====
                        if (IsContainer(item))
                        {
                            itemToPrice = item;
                        }
                        else
                        {
                            itemToPrice = item.CloneItem();
                            itemToPrice.StackObjectsCount = 1;
                        }

                        // 获取商人收购价格（商人向玩家收购的价格）
                        var priceStruct = trader.GetUserItemPrice(itemToPrice);
                        if (!priceStruct.HasValue)
                            continue;

                        int amount = priceStruct.Value.Amount;
                        if (amount <= 0)
                            continue; // 无效价格

                        MongoID? currencyIdNullable = priceStruct.Value.CurrencyId;
                        if (!currencyIdNullable.HasValue)
                            continue; // 无货币信息，忽略

                        MongoID currencyId = currencyIdNullable.Value;

                        double currencyCourse = GetCurrencyCourse(trader, currencyId);

                        double priceInRoubles = amount * currencyCourse;

                        // ===== 新增校验：忽略不合理的大额值（通常来自商人出售列表或错误数据） =====
                        if (priceInRoubles <= 0 || priceInRoubles > MAX_REASONABLE_TRADER_PRICE_ROUBLES)
                        {
                            // skip unrealistic value
                            continue;
                        }

                        // 保存最高价格
                        if (highestPrice == null || priceInRoubles > highestPrice.PriceInRoubles)
                        {
                            highestPrice = new TraderPrice(
                                trader.Id,
                                trader.LocalizedName,
                                amount,
                                currencyId,
                                currencyCourse,
                                priceInRoubles
                            );
                        }
                    }
                    catch (Exception)
                    {
                        // 静默跳过失败的商人
                        continue;
                    }
                }

                if (highestPrice == null && !_hasShownInitTip)
                {
                    _hasShownInitTip = true;
                }

                // ===== 优化3: 保存到缓存 =====
                if (highestPrice != null)
                {
                    _priceCache[cacheKey] = highestPrice;
                }

                return highestPrice;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"❌ 获取商人价格失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 检查物品是否是容器（带反射缓存）
        /// </summary>
        /// <param name="item">物品</param>
        /// <returns>true = 容器, false = 非容器</returns>
        private bool IsContainer(Item item)
        {
            try
            {
                var itemType = item.GetType();

                // ===== 优化4: 从缓存获取反射的 PropertyInfo =====
                // 避免重复反射，每个类型只反射一次
                if (!_containerPropertyCache.TryGetValue(itemType, out var isContainerProperty))
                {
                    // 首次访问：反射获取并缓存
                    isContainerProperty = itemType.GetProperty("IsContainer");
                    _containerPropertyCache[itemType] = isContainerProperty;
                }

                if (isContainerProperty != null)
                {
                    var isContainer = isContainerProperty.GetValue(item);
                    return isContainer is bool boolValue && boolValue;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 清除价格缓存（价格更新时调用）
        /// </summary>
        public void ClearCache()
        {
            _priceCache.Clear();
            // Plugin.Log.LogInfo("🔄 商人价格缓存已清除");
        }

        /// <summary>
        /// 获取缓存统计信息（用于调试）
        /// </summary>
        public string GetCacheStats()
        {
            return $"商人价格缓存: {_priceCache.Count} 项";
        }

        /// <summary>
        /// 获取所有商人
        /// </summary>
        private System.Collections.Generic.IEnumerable<TraderClass> GetAllTraders()
        {
            try
            {
                // 使用 Singleton 获取 ClientApplication
                var clientApp = Singleton<ClientApplication<ISession>>.Instance;
                if (clientApp == null)
                    return null;

                var session = clientApp.GetClientBackEndSession();
                if (session != null && session.Traders != null)
                    return session.Traders;

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 检查商人是否可用
        /// </summary>
        private bool IsTraderAvailable(TraderClass trader)
        {
            try
            {
                return trader != null
                    && trader.Info != null
                    && trader.Info.Available
                    && !trader.Info.Disabled
                    && trader.Info.Unlocked;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取货币汇率
        /// </summary>
        private double GetCurrencyCourse(TraderClass trader, MongoID currencyId)
        {
            try
            {
                var supplyData = trader.GetSupplyData();
                if (supplyData?.CurrencyCourses != null
                    && supplyData.CurrencyCourses.ContainsKey(currencyId))
                {
                    return supplyData.CurrencyCourses[currencyId];
                }

                // 默认汇率为 1（卢布）
                return 1.0;
            }
            catch
            {
                return 1.0;
            }
        }
    }
}
