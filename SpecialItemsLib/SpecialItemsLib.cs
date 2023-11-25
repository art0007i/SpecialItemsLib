using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using FrooxEngine;
using FrooxEngine.Store;
using Elements.Core;
using Newtonsoft.Json;
using FrooxEngine.UIX;

namespace SpecialItemsLib
{
    public class CustomSpecialItem
    {
        // TODO: get this json ignore thing to actually work
        [Newtonsoft.Json.JsonIgnore]
        public InventoryBrowser.SpecialItemType ItemType { get; set; }
        [Newtonsoft.Json.JsonIgnore]
        public string ReadableName { get; set; }
        public Uri Uri;

        public CustomSpecialItem(int type, string readableName)
        {
            ReadableName = readableName;
            ItemType = (InventoryBrowser.SpecialItemType)type;
            Uri = null;
        }
    }

    public class SpecialItemsLib : ResoniteMod
    {
        public override string Name => "SpecialItemsLib";
        public override string Author => "art0007i";
        public override string Version => "2.0.1";
        public override string Link => "https://github.com/art0007i/SpecialItemsLib/";

        public static ModConfiguration config;

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<Dictionary<string, CustomSpecialItem>> CUSTOM_ITEMS_KEY = new ModConfigurationKey<Dictionary<string, CustomSpecialItem>>
                                                                                                            ("custom_items", "Stores the urls of custom special items", 
                                                                                                            () => { return new Dictionary<string, CustomSpecialItem>(); });

        public override void DefineConfiguration(ModConfigurationDefinitionBuilder builder)
        {
            builder
                .AutoSave(true)
                .Version(new Version(1, 0, 0));
        }

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("me.art0007i.SpecialItemsLib");
            harmony.PatchAll();
            config = GetConfiguration();
        }

        private static int CurrentSpecialItem = 20;

        public static Dictionary<string, CustomSpecialItem> CustomItems { get { return config.GetValue(CUSTOM_ITEMS_KEY); } }

        private static readonly List<InventoryBrowser> ActiveBrowsers = new List<InventoryBrowser>();

        public static void RefreshItems()
        {
            foreach (var browser in ActiveBrowsers)
            {
                if (browser.CanInteract(browser.LocalUser))
                {
                    browser.RunSynchronously(() =>
                    {
                        AccessTools.Method(typeof(InventoryBrowser), "ReprocessItems").Invoke(browser, null);
                    });
                }
            }
        }


        /// <summary>
        /// Registers an item to allow it to be favoritable.
        /// The readable name will be shown to users in this format "Set {0}"
        /// </summary>
        /// <param name="item_tag">The tag that items will require to be favoritable</param>
        /// <param name="readable_name">The readable name will be shown to users in this format "Set {0}"</param>
        /// <returns></returns>
        public static CustomSpecialItem RegisterItem(string item_tag, string readable_name)
        {
            if (config == null)
            foreach(var mod in ModLoader.Mods())
            {
                if(mod.GetType() == typeof(SpecialItemsLib))
                {
                    config = mod.GetConfiguration();
                }
            }
            CustomSpecialItem item;
            var dict = CustomItems;
            if (dict.TryGetValue(item_tag, out item))
            {
                item.ItemType = (InventoryBrowser.SpecialItemType)CurrentSpecialItem++;
                item.ReadableName = readable_name;
            }
            else
            {
                item = new CustomSpecialItem(CurrentSpecialItem++, readable_name);
                dict.Add(item_tag, item);
            }
            config.Set(CUSTOM_ITEMS_KEY, dict);
            Debug($"adding new item {item_tag} and special item id {CurrentSpecialItem-1}");
            Debug("Special items count " + CustomItems.Count);
            return item;
        }

        [HarmonyPatch(typeof(InventoryBrowser))]
        // Pink item background updating in inventory
        class InventoryBrowser_ChangeEvents_Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch("ProcessItem")]
            public static void PostProcess(InventoryItemUI item)
            {
                Record record = (Record)AccessTools.Field(item.GetType(), "Item").GetValue(item);
                if (record == null) return;
                Uri uri = record.GetUrl(item.Cloud.Platform);
                InventoryBrowser.SpecialItemType specialItemType = InventoryBrowser.ClassifyItem(item);
                foreach (var customItem in CustomItems)
                {
                    if(customItem.Value.ItemType == 0) continue;
                    if (uri != null && specialItemType == customItem.Value.ItemType && uri == customItem.Value.Uri)
                    {
                        item.NormalColor.Value = InventoryBrowser.FAVORITE_COLOR;
                        item.SelectedColor.Value = InventoryBrowser.FAVORITE_COLOR.MulA(2f);
                        return;
                    }
                }
            }

            // Keep track of active inventory browsers to refresh them when you click the pink button
            [HarmonyPostfix]
            [HarmonyPatch("OnAwake")]
            public static void PostAwake(InventoryBrowser __instance)
            {

                ActiveBrowsers.Add(__instance);

            }
            [HarmonyPostfix]
            [HarmonyPatch("OnDispose")]
            public static void PostDispose(InventoryBrowser __instance)
            {
                ActiveBrowsers.Remove(__instance);
            }
        }

        private static void ReprocessItems()
        {
            ActiveBrowsers.ForEach(browser =>
            {
                if (browser.CanInteract(browser.LocalUser))
                {
                    browser.RunSynchronously(() =>
                    {
                        AccessTools.Method(typeof(InventoryBrowser), "ReprocessItems").Invoke(browser, null);
                    });
                }
            });
        }

        // This allows identifying which items in the inventory are custom
        [HarmonyPatch(typeof(InventoryBrowser), "ClassifyItem")]
        class InventoryBrowser_ClassifyItem_Patch
        {
            public static void Postfix(InventoryItemUI itemui, ref InventoryBrowser.SpecialItemType __result)
            {
                // For people who have videos on their avatar
                if (__result != InventoryBrowser.SpecialItemType.None) return;
                if (itemui != null)
                {
                    Record record = (Record)AccessTools.Field(itemui.GetType(), "Item").GetValue(itemui);
                    if (record != null && record.Tags != null)
                    {
                        foreach(var item in CustomItems)
                        {
                            if (item.Value.ItemType == 0) continue;
                            if (record.Tags.Contains(item.Key))
                            {
                                __result = item.Value.ItemType;
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(InventoryBrowser), "OnItemSelected")]
        // Adds the favourite button to custom special items
        class InventoryBrowser_OnItemSelected_Patch
        {
            public static void Prefix(InventoryBrowser __instance, out InventoryBrowser.SpecialItemType __state)
            {
                __state = (AccessTools.Field(typeof(InventoryBrowser), "_lastSpecialItemType").GetValue(__instance) as Sync<InventoryBrowser.SpecialItemType>).Value;
            }

            public static void Postfix(InventoryBrowser __instance, BrowserItem currentItem, InventoryBrowser.SpecialItemType __state)
            {
                InventoryItemUI inventoryItemUI = currentItem as InventoryItemUI;
                InventoryBrowser.SpecialItemType specialItemType = InventoryBrowser.ClassifyItem(inventoryItemUI);
                var buttonsRoot = (AccessTools.Field(typeof(InventoryBrowser), "_buttonsRoot").GetValue(__instance) as SyncRef<Slot>).Target[0];
                Debug("Classified item is " + specialItemType);
                if (__state == specialItemType) return;
                var dict = CustomItems;
                Debug(dict.Count);
                foreach(var item in dict)
                {
                    Debug("checking against " + item.Value.ItemType);
                    if (item.Value.ItemType == 0) continue;
                    if (specialItemType == item.Value.ItemType)
                    {
                        var uibuilder = new FrooxEngine.UIX.UIBuilder(buttonsRoot);
                        uibuilder.Style.PreferredWidth = BrowserDialog.DEFAULT_ITEM_SIZE * 3;
                        RadiantUI_Constants.SetupDefaultStyle(uibuilder);

                        //MixColor method, since its a one liner i would rather just copy source than reflection to get it
                        var pink = MathX.Lerp(colorX.Purple, colorX.White, 0.5f);

                        var but = uibuilder.Button(OfficialAssets.Graphics.Icons.Inspector.Pin, $"Set {item.Value.ReadableName}");

                        but.Slot.OrderOffset = -1;
                        but.LocalPressed += (IButton button, ButtonEventData data) => {
                            Uri url = (AccessTools.Field(typeof(InventoryItemUI), "Item").GetValue(__instance.SelectedInventoryItem) as Record).GetUrl(but.Cloud.Platform);
                            if (item.Value.Uri == url)
                            {
                                url = null;
                            }
                            item.Value.Uri = url;
                            config.Set(CUSTOM_ITEMS_KEY, dict);
                            ReprocessItems();
                        };
                        break;
                    }
                }
            }
        }
    }
}