﻿//
//  WinCompose — a compose key for Windows — http://wincompose.info/
//
//  Copyright © 2013—2016 Sam Hocevar <sam@hocevar.net>
//              2014—2015 Benjamin Litzelmann
//
//  This program is free software. It comes without any warranty, to
//  the extent permitted by applicable law. You can redistribute it
//  and/or modify it under the terms of the Do What the Fuck You Want
//  to Public License, Version 2, as published by the WTFPL Task Force.
//  See http://www.wtfpl.net/ for more details.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WinCompose
{
    public static class Settings
    {
        private const string GlobalSection = "global";
        private const string ConfigFileName = "settings.ini";
        private static FileSystemWatcher m_watcher;
        private static Timer m_reload_timer;

        static Settings()
        {
            Language = new SettingsEntry<string>(GlobalSection, "language", "");
            ComposeKey = new SettingsEntry<Key>(GlobalSection, "compose_key", m_default_compose_key);
            ResetDelay = new SettingsEntry<int>(GlobalSection, "reset_delay", -1);
            Disabled = new SettingsEntry<bool>(GlobalSection, "disabled", false);
            UnicodeInput = new SettingsEntry<bool>(GlobalSection, "unicode_input", true);
            CaseInsensitive = new SettingsEntry<bool>(GlobalSection, "case_insensitive", false);
            DiscardOnInvalid = new SettingsEntry<bool>(GlobalSection, "discard_on_invalid", false);
            BeepOnInvalid = new SettingsEntry<bool>(GlobalSection, "beep_on_invalid", false);
            KeepOriginalKey = new SettingsEntry<bool>(GlobalSection, "keep_original_key", false);
            InsertZwsp = new SettingsEntry<bool>(GlobalSection, "insert_zwsp", false);
            EmulateCapsLock = new SettingsEntry<bool>(GlobalSection, "emulate_capslock", false);
            ShiftDisablesCapsLock = new SettingsEntry<bool>(GlobalSection, "shift_disables_capslock", false);
            CapsLockCapitalizes = new SettingsEntry<bool>(GlobalSection, "capslock_capitalizes", false);
        }

        public static SettingsEntry<string> Language { get; private set; }

        public static SettingsEntry<Key> ComposeKey { get; private set; }

        public static SettingsEntry<int> ResetDelay { get; private set; }

        public static SettingsEntry<bool> Disabled { get; private set; }

        public static SettingsEntry<bool> UnicodeInput { get; private set; }

        public static SettingsEntry<bool> CaseInsensitive { get; private set; }

        public static SettingsEntry<bool> DiscardOnInvalid { get; private set; }

        public static SettingsEntry<bool> BeepOnInvalid { get; private set; }

        public static SettingsEntry<bool> KeepOriginalKey { get; private set; }

        public static SettingsEntry<bool> InsertZwsp { get; private set; }

        public static SettingsEntry<bool> EmulateCapsLock { get; private set; }

        public static SettingsEntry<bool> ShiftDisablesCapsLock { get; private set; }

        public static SettingsEntry<bool> CapsLockCapitalizes { get; private set; }

        public static IEnumerable<Key> ValidComposeKeys { get { return m_valid_compose_keys; } }

        public static Dictionary<string, string> ValidLanguages { get { return m_valid_languages; } }

        public static void StartWatchConfigFile()
        {
            if (CreateConfigDir())
            {
                m_watcher = new FileSystemWatcher(GetConfigDir(), ConfigFileName);
                m_watcher.Changed += ConfigFileChanged;
                m_watcher.EnableRaisingEvents = true;
            }
        }

        public static void StopWatchConfigFile()
        {
            if (m_watcher != null)
            {
                m_watcher.Dispose();
                m_watcher = null;
            }
        }

        private static void ConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            if (m_reload_timer == null)
            {
                // This event is triggered multiple times.
                // Let's defer its handling to reload the config only once.
                m_reload_timer = new Timer(ReloadConfig, null, 300, Timeout.Infinite);
            }
        }

        private static void ReloadConfig(object state)
        {
            m_reload_timer.Dispose();
            m_reload_timer = null;
            LoadConfig();
        }

        public static void LoadConfig()
        {
            // The key used as the compose key
            ComposeKey.Load();

            if (!m_valid_compose_keys.Contains(ComposeKey.Value))
                ComposeKey.Value = m_default_compose_key;

            // The timeout delay
            ResetDelay.Load();

            // Activate the desired interface language
            Language.Load();

            // HACK: if the user uses the "it-CH" locale, replace it with "it"
            // because we use "it-CH" as a special value to mean Sardinian.
            // The reason is that apparently we cannot define a custom
            // CultureInfo without registering it, and we cannot register it
            // without administrator privileges.
            if (Thread.CurrentThread.CurrentUICulture.Name == "it-CH")
            {
                try
                {
                    Thread.CurrentThread.CurrentUICulture
                        = Thread.CurrentThread.CurrentUICulture.Parent;
                }
                catch (Exception) { }
            }

            if (Language.Value != "")
            {
                if (m_valid_languages.ContainsKey(Language.Value))
                {
                    try
                    {
                        var ci = CultureInfo.GetCultureInfo(Language.Value);
                        Thread.CurrentThread.CurrentUICulture = ci;
                    }
                    catch (Exception) { }
                }
                else
                {
                    Language.Value = "";
                }
            }

            // Catch-22: we can only add this string when the UI language is known
            m_valid_languages[""] = i18n.Text.AutodetectLanguage;

            // Various options
            Disabled.Load();
            UnicodeInput.Load();
            CaseInsensitive.Load();
            DiscardOnInvalid.Load();
            BeepOnInvalid.Load();
            KeepOriginalKey.Load();
            InsertZwsp.Load();
            EmulateCapsLock.Load();
            ShiftDisablesCapsLock.Load();
            CapsLockCapitalizes.Load();
        }

        public static void SaveConfig()
        {
            SaveEntry("reset_delay", m_delay.ToString());
            Language.Save();
            ComposeKey.Save();
            Disabled.Save();
            UnicodeInput.Save();
            CaseInsensitive.Save();
            DiscardOnInvalid.Save();
            BeepOnInvalid.Save();
            KeepOriginalKey.Save();
            InsertZwsp.Save();
            EmulateCapsLock.Save();
            ShiftDisablesCapsLock.Save();
            CapsLockCapitalizes.Save();
        }

        public static void LoadSequences()
        {
            m_sequences = new SequenceTree();
            m_sequence_count = 0;

            LoadSequenceFile(Path.Combine(GetDataDir(), "Xorg.txt"));
            LoadSequenceFile(Path.Combine(GetDataDir(), "XCompose.txt"));
            LoadSequenceFile(Path.Combine(GetDataDir(), "Emoji.txt"));
            LoadSequenceFile(Path.Combine(GetDataDir(), "WinCompose.txt"));

            LoadSequenceFile(Path.Combine(GetUserDir(), ".XCompose"));
            LoadSequenceFile(Path.Combine(GetUserDir(), ".XCompose.txt"));
        }

        /// <summary>
        /// Find the preferred application for .txt files and launch it to
        /// open .XCompose. If the file does not exist, try .XCompose.txt
        /// instead. If it doesn’t exist either, create .XCompose ourselves.
        /// </summary>
        public static void EditCustomRuleFile()
        {
            // Ensure the rules file exists.
            string user_file = Path.Combine(GetUserDir(), ".XCompose");
            if (!File.Exists(user_file))
            {
                string alt_file = Path.Combine(GetUserDir(), ".XCompose.txt");
                if (File.Exists(alt_file))
                {
                    user_file = alt_file;
                }
                else
                {
                    using (var s = File.OpenWrite(user_file))
                    using (var sw = new StreamWriter(s, new UTF8Encoding(true)))
                    {
                        sw.WriteLine("# Custom rule file for WinCompose");
                        sw.WriteLine("#");
                        sw.WriteLine("#");
                        sw.WriteLine("# Sample rule. Remove leading “#” to activate.");
                        sw.WriteLine("#<Multi_key> <h> <a> : \"ha ha ha ha\"");
                    }
                }
            }

            // Find the preferred application for .txt files
            uint length = 0, ret;
            ret = NativeMethods.AssocQueryString(ASSOCF.NONE,
                            ASSOCSTR.EXECUTABLE, ".txt", null, null, ref length);
            if (ret != 0)
                return;

            var sb = new StringBuilder((int)length);
            ret = NativeMethods.AssocQueryString(ASSOCF.NONE,
                            ASSOCSTR.EXECUTABLE, ".txt", null, sb, ref length);
            if (ret != 0)
                return;

            // Open the rules file with that application
            var psinfo = new ProcessStartInfo
            {
                FileName = sb.ToString(),
                Arguments = user_file,
                UseShellExecute = true,
            };
            Process.Start(psinfo);
        }

        public static SequenceTree GetSequenceList()
        {
            return m_sequences;
        }

        public static bool IsValidPrefix(KeySequence sequence, bool ignore_case)
        {
            return m_sequences.IsValidPrefix(sequence, ignore_case);
        }

        public static bool IsValidSequence(KeySequence sequence, bool ignore_case)
        {
            return m_sequences.IsValidSequence(sequence, ignore_case);
        }

        public static string GetSequenceResult(KeySequence sequence, bool ignore_case)
        {
            return m_sequences.GetSequenceResult(sequence, ignore_case);
        }

        public static List<SequenceDescription> GetSequenceDescriptions()
        {
            return m_sequences.GetSequenceDescriptions();
        }

        public static int GetSequenceCount()
        {
            return m_sequence_count;
        }

        private static string LoadEntry(string key)
        {
            const int len = 255;
            var tmp = new StringBuilder(len);
            NativeMethods.GetPrivateProfileString("global", key, "",
                                                  tmp, len, GetConfigFile());
            return tmp.ToString();
        }

        private static void SaveEntry(string key, string val)
        {
            NativeMethods.WritePrivateProfileString("global", key, val,
                                                    GetConfigFile());
        }

        private static void LoadSequenceFile(string path)
        {
            try
            {
                foreach (string line in File.ReadAllLines(path))
                    LoadSequenceString(line);
            }
            catch (Exception) { }
        }

        private static void LoadSequenceString(string line)
        {
            // Only bother with sequences that start with <Multi_key>
            var m1 = Regex.Match(line, @"^\s*<Multi_key>\s*([^:]*):[^""]*""(([^""]|\\"")*)""[^#]*#?\s*(.*)");
            //                                             ^^^^^^^         ^^^^^^^^^^^^^^^            ^^^^
            //                                              keys               result                 desc
            if (m1.Groups.Count < 4)
                return;

            var keysyms = Regex.Split(m1.Groups[1].Captures[0].ToString(), @"[\s<>]+");

            if (keysyms.Length < 4) // We need 2 empty strings + at least 2 keysyms
                return;

            KeySequence seq = new KeySequence();

            for (int i = 1; i < keysyms.Length; ++i)
            {
                if (keysyms[i] == String.Empty)
                    continue;

                Key k = Key.FromKeySym(keysyms[i]);
                if (k == null)
                {
                    //Console.WriteLine("Unknown key name <{0}>, ignoring sequence", keysyms[i]);
                    return; // Unknown key name! Better bail out
                }

                seq.Add(k);
            }

            string result = m1.Groups[2].Captures[0].ToString();
            string description = m1.Groups.Count >= 5 ? m1.Groups[4].Captures[0].ToString() : "";

            // Replace \\ and \" in the string output
            result = new Regex(@"\\(\\|"")").Replace(result, "$1");

            // Try to translate the description if appropriate
            int utf32 = StringToCodepoint(result);
            if (utf32 >= 0)
            {
                string key = String.Format("U{0:X04}", utf32);
                string alt_desc = unicode.Char.ResourceManager.GetString(key);
                if (alt_desc != null && alt_desc.Length > 0)
                    description = alt_desc;
            }

            m_sequences.Add(seq, result, utf32, description);
            ++m_sequence_count;
        }

        private static int StringToCodepoint(string s)
        {
            if (s.Length == 1)
                return (int)s[0];

            if (s.Length == 2 && s[0] >= 0xd800 && s[0] <= 0xdbff)
                return Char.ConvertToUtf32(s[0], s[1]);

            return -1;
        }

        // Tree of all known sequences
        private static SequenceTree m_sequences = new SequenceTree();
        private static int m_sequence_count = 0;

        // FIXME: couldn't we accept any compose key?
        private static readonly KeySequence m_valid_compose_keys = new KeySequence
        {
           new Key(VK.LMENU),
           new Key(VK.RMENU),
           new Key(VK.LCONTROL),
           new Key(VK.RCONTROL),
           new Key(VK.LWIN),
           new Key(VK.RWIN),
           new Key(VK.CAPITAL),
           new Key(VK.NUMLOCK),
           new Key(VK.PAUSE),
           new Key(VK.APPS),
           new Key(VK.ESCAPE),
           new Key(VK.INSERT),
           new Key(VK.SCROLL),
           new Key("`"),
        };

        private static readonly Key m_default_compose_key = new Key(VK.RMENU);

        private static readonly
        Dictionary<string, string> m_valid_languages = GetSupportedLanguages();

        private static readonly Dictionary<int, string> m_valid_delays
         = new Dictionary<int, string>()
        {
            { 500,   "500 milliseconds" },
            { 1000,  "1 second" },
            { 2000,  "2 seconds" },
            { 3000,  "3 seconds" },
            { 5000,  "5 seconds" },
            { 10000, "10 seconds" },
            { -1,    "None" },
        };

        private static int m_delay = -1;

        private static Dictionary<string, string> GetSupportedLanguages()
        {
            Dictionary<string, string> ret = new Dictionary<string, string>()
            {
                { "en", "English" },
            };

            // Enumerate all languages that have an embedded resource file version
            ResourceManager rm = new ResourceManager(typeof(i18n.Text));
            CultureInfo[] cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
            foreach (CultureInfo ci in cultures)
            {
                string name = ci.Name;
                string native_name = ci.TextInfo.ToTitleCase(ci.NativeName);

                if (name != "") try
                {
                    if (rm.GetResourceSet(ci, true, false) != null)
                    {
                        // HACK: second part of our hack to support Sardinian
                        if (name == "it-CH")
                            native_name = "Sardu";

                        ret.Add(name, native_name);
                    }
                }
                catch (Exception) {}
            }

            return ret;
        }

        public static string GetConfigFile()
        {
            return Path.Combine(GetConfigDir(), ConfigFileName);
        }

        public static bool CreateConfigDir()
        {
            string config_dir = GetConfigDir();
            if (!Directory.Exists(config_dir))
            {
                try
                {
                    Directory.CreateDirectory(config_dir);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return true;
        }

        private static string GetConfigDir()
        {
            var appdata = Environment.SpecialFolder.ApplicationData;
            var appdatadir = Path.Combine(Environment.GetFolderPath(appdata),
                                          "WinCompose");
            return IsInstalled() ? appdatadir : GetExeDir();
        }

        private static string GetDataDir()
        {
            return IsInstalled() ? Path.Combine(GetExeDir(), "res")
                 : IsDebugging() ? Path.Combine(GetExeDir(), "../../rules")
                 : Path.Combine(GetExeDir(), "rules");
        }

        private static string GetUserDir()
        {
            return Environment.ExpandEnvironmentVariables("%USERPROFILE%");
        }

        private static string GetExeName()
        {
            var codebase = Assembly.GetExecutingAssembly().GetName().CodeBase;
            return Uri.UnescapeDataString(new UriBuilder(codebase).Path);
        }

        private static string GetExeDir()
        {
            return Path.GetDirectoryName(GetExeName());
        }

        private static bool IsInstalled()
        {
            return File.Exists(Path.Combine(GetExeDir(), "unins000.dat"));
        }

        private static bool IsDebugging()
        {
            string exe = GetExeName();
            return File.Exists(Path.ChangeExtension(exe, ".pdb"));
        }
    }
}
