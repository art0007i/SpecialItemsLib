using HarmonyLib;
using NeosModLoader;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using FrooxEngine;
using BaseX;
using Newtonsoft.Json;

namespace SpecialItemsLib
{
    public class CustomSpecialItem
    {
        public InventoryBrowser.SpecialItemType ItemType;
        public string Tag;
        public string Variable;
        private Uri _uri;
        public Uri Uri
        {
            get
            {
                return _uri;
            }
            set
            {
                if (!SpecialItemsLib.config.GetValue(SpecialItemsLib.USE_CLOUD_KEY) || Variable == null)
                {
                    _uri = value;
                    return;
                }
                if (_uri != value)
                {
                    if (Engine.Current.Cloud.CurrentUser == null && value != null)
                    {
                        throw new InvalidOperationException($"Cannot set special item {ItemType} URL without being signed in");
                    }
                    _uri = value;
                    if (Engine.Current.Cloud.CurrentUser != null)
                    {
                        Engine.Current.Cloud.WriteVariable(Variable, value);
                    }
                    SpecialItemsLib.RefreshItems();
                }
            }
        }

        public CustomSpecialItem(int type, string tag)
        {
            ItemType = (InventoryBrowser.SpecialItemType)type;
            Tag = tag;
            Variable = null;
            _uri = null;
        }

        public CustomSpecialItem(int type, string tag, string var)
        {
            ItemType = (InventoryBrowser.SpecialItemType)type;
            Tag = tag;
            Variable = var;
            _uri = null;
        }
    }

    public class SpecialItemsLib : NeosMod
    {
        public override string Name => "SpecialItemsLib";
        public override string Author => "art0007i";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/art0007i/SpecialItemsLib/";

        public static readonly ModConfigurationKey<bool> USE_CLOUD_KEY = new ModConfigurationKey<bool>("use_cloud", "Wether to use cloud variables to store special items (when available)", () => false);
        public static ModConfiguration config;

        public override ModConfigurationDefinition GetConfigurationDefinition()
        {
            var keys = new List<ModConfigurationKey> { USE_CLOUD_KEY };
            return DefineConfiguration(new Version(1, 0, 0), keys);
        }

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("me.art0007i.SpecialItemsLib");
            harmony.PatchAll();
            config = GetConfiguration();
        }

        private static int CurrentSpecialItem = 20;
        private static readonly List<CustomSpecialItem> CustomItems = new List<CustomSpecialItem>();
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

        public static CustomSpecialItem RegisterItem(string item_tag) => RegisterItem(item_tag, null);
        public static CustomSpecialItem RegisterItem(string item_tag, string variable_name)
        {
            var item = new CustomSpecialItem(CurrentSpecialItem++, item_tag, variable_name);
            CustomItems.Add(item);
            Msg($"adding new item {item_tag} with var name {variable_name} and special item id {CurrentSpecialItem}");
            Msg("Special items count " + CustomItems.Count);
            return item;
        }

        [HarmonyPatch(typeof(ProfileManager), "SignIn")]
        // Loads custom item urls from cloud
        class ProfileManager_SignIn_Patch
        {
            public static async void Prefix(ProfileManager __instance)
            {
                foreach (var item in CustomItems)
                {
                    if(item.Variable != null && config.GetValue(USE_CLOUD_KEY))
                    {
                        var cloudResult = await __instance.Cloud.ReadVariable<Uri>(item.Variable);
                        if (cloudResult.IsOK)
                        {
                            item.Uri = cloudResult.Entity;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(InventoryBrowser))]
        // Pink item background updating in inventory
        class InventoryBrowser_ChangeEvents_Patch
        {

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

            [HarmonyPostfix]
            [HarmonyPatch("ProcessItem")]
            public static void PostProcess(InventoryItemUI item)
            {
                Record record = (Record)AccessTools.Field(item.GetType(), "Item").GetValue(item);
                if (record == null) return;
                Uri uri = record.URL;
                InventoryBrowser.SpecialItemType specialItemType = InventoryBrowser.ClassifyItem(item);
                foreach(var customItem in CustomItems)
                {
                    if (uri != null && specialItemType == customItem.ItemType && uri == customItem.Uri)
                    {
                        item.NormalColor.Value = InventoryBrowser.ACTIVE_AVATAR_COLOR;
                        item.SelectedColor.Value = InventoryBrowser.ACTIVE_AVATAR_COLOR.MulA(2f);
                        return;
                    }
                }
            }
        }

        // This allows identifying which items in the inventory are custom
        [HarmonyPatch(typeof(InventoryBrowser), "ClassifyItem")]
        class InventoryBrowser_ClassifyItem_Patch
        {
            public static void Postfix(InventoryItemUI itemui, ref InventoryBrowser.SpecialItemType __result)
            {
                if (itemui != null)
                {
                    Record record = (Record)AccessTools.Field(itemui.GetType(), "Item").GetValue(itemui);
                    if (record != null && record.Tags != null)
                    {
                        foreach(var item in CustomItems)
                        {
                            if (record.Tags.Contains(item.Tag))
                            {
                                __result = item.ItemType;
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
                Msg("State is " + __state);
            }

            public static void Postfix(InventoryBrowser __instance, BrowserItem currentItem, InventoryBrowser.SpecialItemType __state)
            {
                InventoryItemUI inventoryItemUI = currentItem as InventoryItemUI;
                InventoryBrowser.SpecialItemType specialItemType = InventoryBrowser.ClassifyItem(inventoryItemUI);
                var buttonsRoot = (AccessTools.Field(typeof(InventoryBrowser), "_buttonsRoot").GetValue(__instance) as SyncRef<Slot>).Target[0];
                Msg("Classified item is " + specialItemType);
                if (__state == specialItemType) return;
                Msg(CustomItems.Count);
                foreach(var item in CustomItems)
                {
                    Msg("checking against " + item.ItemType);
                    if (specialItemType == item.ItemType)
                    {
                        var uibuilder = new FrooxEngine.UIX.UIBuilder(buttonsRoot);
                        uibuilder.Style.PreferredWidth = BrowserDialog.DEFAULT_ITEM_SIZE * 0.6f;

                        //MixColor method, since its a one liner i would rather just copy source than reflection to get it
                        var pink = MathX.Lerp(color.Purple, color.White, 0.5f);

                        var but = uibuilder.Button(NeosAssets.Common.Icons.Heart, pink, color.Black);

                        but.Slot.OrderOffset = -1;
                        but.LocalPressed += (IButton button, ButtonEventData data) => {
                            Uri url = (AccessTools.Field(typeof(InventoryItemUI), "Item").GetValue(__instance.SelectedInventoryItem) as Record).URL;
                            if (item.Uri == url)
                            {
                                url = null;
                            }
                            item.Uri = url;
                        };
                        break;
                    }
                }
            }
        }
    }
}