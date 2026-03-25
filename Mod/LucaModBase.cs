// <copyright file="LucaModBase.cs" company="Luca Rager">
// Copyright (c) Luca Rager. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace LucaModsCommon.Mod {
    #region Using Statements

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    using Colossal;
    using Colossal.IO.AssetDatabase;
    using Colossal.Json;
    using Colossal.Localization;
    using Colossal.Logging;
    using Colossal.Reflection;
    using Colossal.TestFramework;
    using Colossal.UI;

    using Game;
    using Game.Modding;
    using Game.SceneFlow;

    using HarmonyLib;

    using LucaModsCommon.Extensions;
    using LucaModsCommon.Utils;

    using Newtonsoft.Json;

    using UnityEngine;

    using StreamReader = System.IO.StreamReader;

    #endregion

    /// <summary>
    /// Abstract base class for Luca's CS2 mods. Provides common lifecycle management including
    /// logging, settings, localization, Harmony patching, asset registration, and test registration.
    /// </summary>
    /// <typeparam name="TSelf">The derived mod type, enabling typed static Instance access.</typeparam>
    public abstract class LucaModBase<TSelf> : IMod where TSelf : LucaModBase<TSelf> {
        private Harmony        m_Harmony;
        private PrefixedLogger m_Log;

        /// <summary>
        /// Gets the singleton instance of the mod.
        /// </summary>
        public static TSelf Instance { get; private set; }

        /// <summary>
        /// Gets the mod's raw logger.
        /// </summary>
        public ILog Log { get; private set; }

        /// <summary>
        /// Gets the mod's prefixed logger.
        /// </summary>
        public PrefixedLogger ModLog => m_Log;

        /// <summary>
        /// Gets the mod's settings. Use <c>new</c> keyword in derived class to shadow with typed property.
        /// </summary>
        public ModSetting Settings { get; protected set; }

        /// <summary>
        /// Gets or sets a value indicating whether the mod is in test mode.
        /// </summary>
        protected internal bool IsTestMode { get; set; } = false;

        #region Abstract Members

        /// <summary>
        /// Gets the mod's display name (e.g., "Platter", "NetworkTools").
        /// </summary>
        public abstract string ModName { get; }

        /// <summary>
        /// Gets the mod's binding Id used for UI bindings between C# and TypeScript.
        /// </summary>
        public abstract string Id { get; }

        /// <summary>
        /// Gets the UI host location prefix (e.g., "platter", "nt") used for AddHostLocation.
        /// </summary>
        protected abstract string UiHostPrefix { get; }

        /// <summary>
        /// Creates the mod-specific settings instance.
        /// </summary>
        /// <param name="mod">The mod instance.</param>
        /// <returns>A new settings instance.</returns>
        protected abstract ModSetting CreateSettings(IMod mod);

        /// <summary>
        /// Creates the en-US localization dictionary source.
        /// </summary>
        /// <param name="settings">The mod's settings.</param>
        /// <returns>The en-US dictionary source.</returns>
        protected abstract IDictionarySource CreateEnUsLocalization(ModSetting settings);

        /// <summary>
        /// Registers all mod-specific ECS systems with the update system.
        /// </summary>
        /// <param name="updateSystem">The update system to register into.</param>
        protected abstract void RegisterSystems(UpdateSystem updateSystem);

        #endregion

        #region Virtual Hooks

        /// <summary>
        /// Called after core initialization (logger, settings, localization) but before
        /// Harmony patching and system registration. Override for mod-specific post-init logic.
        /// </summary>
        /// <param name="updateSystem">The update system.</param>
        protected virtual void OnAfterLoad(UpdateSystem updateSystem) { }

        /// <summary>
        /// Called at the start of OnDispose, before settings unregistration and Harmony teardown.
        /// Override for mod-specific cleanup.
        /// </summary>
        protected virtual void OnBeforeDispose() { }

        /// <summary>
        /// Override to generate a language file in debug/I18N builds. The base implementation is a no-op.
        /// Derived mods should use <c>[CallerFilePath]</c> to determine the export directory.
        /// </summary>
        protected virtual void GenerateLanguageFile() { }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the mod's informational version from assembly metadata.
        /// </summary>
        public string InformationalVersion =>
            GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        /// <summary>
        /// Gets the mod's version string.
        /// </summary>
        public string Version => GetType().Assembly.GetName().Version?.ToString(4) ?? "0.0.0.0";

        private string HarmonyPatchId => $"{ModName}.{GetType().Name}";

        #endregion

        /// <inheritdoc/>
        public void OnLoad(UpdateSystem updateSystem) {
            // Set instance reference.
            Instance = (TSelf)this;

            // Initialize logger.
            Log = LogManager
                  .GetLogger(ModName)
                  .SetShowsErrorsInUI(false);
#if IS_DEBUG
            Log = Log
                  .SetBacktraceEnabled(true)
                  .SetEffectiveness(Level.All);
#endif
            m_Log = new PrefixedLogger(GetType().Name, Log);
            m_Log.Info($"Loading {ModName} version {GetType().Assembly.GetName().Version}");

            // Initialize shared utilities.
            ReflectionExtensions.Initialize(Log);

            // Initialize Settings.
            Settings = CreateSettings(this);

            // Load i18n.
            GameManager.instance.localizationManager.AddSource("en-US", CreateEnUsLocalization(Settings));
            LoadNonEnglishLocalizations();

            // Generate i18n files in debug builds.
#if IS_DEBUG && EXPORT_EN_US
            GenerateLanguageFile();
#endif

            // Register mod settings to game options UI.
            Settings.RegisterInOptionsUI();

            // Load saved settings.
            AssetDatabase.global.LoadSettings(ModName, Settings, CreateSettings(this));

            // Apply input bindings.
            Settings.RegisterKeyBindings();

            // Mod-specific post-init hook.
            OnAfterLoad(updateSystem);

            // Harmony patches.
            InitializeHarmonyPatches();

            // Register systems.
            RegisterSystems(updateSystem);

            // Add tests.
#if IS_DEBUG
            AddTests();
#endif

            // Register UI assets.
            RegisterAssets();

            m_Log.Info($"Installed and enabled. RenderedFrame: {Time.renderedFrameCount}");
        }

        /// <inheritdoc/>
        public void OnDispose() {
            m_Log.Info("OnDispose()");

            OnBeforeDispose();

            Instance = null;

            // Clear settings menu entry.
            if (Settings != null) {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }

            // Unpatch Harmony.
            TeardownHarmonyPatches();
        }

        /// <summary>
        /// Initializes Harmony patches for the assembly and logs all patched methods.
        /// </summary>
        private void InitializeHarmonyPatches() {
            m_Log.Debug("InitializeHarmonyPatches()");

            m_Harmony = new Harmony(HarmonyPatchId);
            m_Harmony.PatchAll(GetType().Assembly);
            var patchedMethods = m_Harmony.GetPatchedMethods().ToArray();

            foreach (var patchedMethod in patchedMethods) {
                m_Log.Debug($"InitializeHarmonyPatches() -- Patched method: {patchedMethod.Module.ScopeName}:{patchedMethod.Name}");
            }
        }

        /// <summary>
        /// Removes all Harmony patches applied by this mod.
        /// </summary>
        private void TeardownHarmonyPatches() {
            m_Log.Debug("TeardownHarmonyPatches()");
            m_Harmony?.UnpatchAll(HarmonyPatchId);
        }

        /// <summary>
        /// Registers the mod's asset files with the game's UI system.
        /// </summary>
        private void RegisterAssets() {
            m_Log.Debug("RegisterAssets()");
            if (!GameManager.instance.modManager.TryGetExecutableAsset(this, out var modAsset)) {
                m_Log.Error("Failed to get executable asset path. Exiting.");
                return;
            }

            var assemblyPath = Path.GetDirectoryName(modAsset.GetMeta().path);
            UIManager.defaultUISystem.AddHostLocation(UiHostPrefix, assemblyPath + "/Assets/");
        }

        /// <summary>
        /// Adds test scenarios from the mod's assembly to the test framework.
        /// </summary>
        private void AddTests() {
            m_Log.Debug("AddTests()");

            var field = typeof(TestScenarioSystem).GetField("m_Scenarios", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) {
                m_Log.Error("AddTests() -- Could not find m_Scenarios");
                return;
            }

            var scenarios = (Dictionary<string, TestScenarioSystem.Scenario>)field.GetValue(TestScenarioSystem.instance);

            foreach (var type in GetTests()) {
                if (!type.IsClass || type.IsAbstract || type.IsInterface || !type.TryGetAttribute(
                        out TestDescriptorAttribute testDescriptorAttribute)) {
                    continue;
                }

                m_Log.Debug($"AddTests() -- {testDescriptorAttribute.description}");

                scenarios.Add(
                    testDescriptorAttribute.description,
                    new TestScenarioSystem.Scenario {
                        category  = testDescriptorAttribute.category,
                        testPhase = testDescriptorAttribute.testPhase,
                        test      = type,
                        disabled  = testDescriptorAttribute.disabled,
                    });
            }

            scenarios = TestScenarioSystem.SortScenarios(scenarios);

            field.SetValue(TestScenarioSystem.instance, scenarios);
        }

        /// <summary>
        /// Retrieves all test types from the mod's assembly that implement the TestScenario interface.
        /// </summary>
        /// <returns>An enumerable collection of test scenario types.</returns>
        private IEnumerable<Type> GetTests() {
            return from t in GetType().Assembly.GetTypes()
                where typeof(TestScenario).IsAssignableFrom(t)
                select t;
        }

        /// <summary>
        /// Loads non-English localization files from the mod's embedded resources.
        /// </summary>
        private void LoadNonEnglishLocalizations() {
            var modAssembly   = GetType().Assembly;
            var resourceNames = modAssembly.GetManifestResourceNames();

            try {
                m_Log.Debug("Reading localizations");

                foreach (var localeID in GameManager.instance.localizationManager.GetSupportedLocales()) {
                    // Try multiple resource name patterns to support different folder structures.
                    string resourceName = null;
                    foreach (var candidate in new[] {
                        $"{modAssembly.GetName().Name}.L10n.lang.{localeID}.json",
                        $"{modAssembly.GetName().Name}.lang.{localeID}.json",
                    }) {
                        if (resourceNames.Contains(candidate)) {
                            resourceName = candidate;
                            break;
                        }
                    }

                    if (resourceName != null) {
                        m_Log.Debug($"Found localization file {resourceName}");
                        try {
                            m_Log.Debug($"Reading embedded translation file {resourceName}");

                            // Read embedded file.
                            using StreamReader reader = new(modAssembly.GetManifestResourceStream(resourceName));
                            {
                                var entireFile   = reader.ReadToEnd();
                                var varient      = JSON.Load(entireFile);
                                var translations = varient.Make<Dictionary<string, string>>();
                                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(translations));
                            }
                        } catch (Exception e) {
                            // Don't let a single failure stop us.
                            m_Log.Error($"Exception reading localization from embedded file {resourceName}: {e}");
                        }
                    } else {
                        m_Log.Debug($"Did not find localization file for {localeID}");
                    }
                }
            } catch (Exception e) {
                m_Log.Error($"Exception reading embedded settings localization files: {e}");
            }
        }
    }
}
