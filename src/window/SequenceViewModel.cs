//
//  WinCompose � a compose key for Windows � http://wincompose.info/
//
//  Copyright � 2013�2015 Sam Hocevar <sam@hocevar.net>
//              2014�2015 Benjamin Litzelmann
//
//  This program is free software. It comes without any warranty, to
//  the extent permitted by applicable law. You can redistribute it
//  and/or modify it under the terms of the Do What the Fuck You Want
//  to Public License, Version 2, as published by the WTFPL Task Force.
//  See http://www.wtfpl.net/ for more details.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace WinCompose
{
    public class SequenceViewModel
    {
        /// <summary>
        /// A dictionary of string representation of the <see cref="UnicodeCategory"/> enum, used to prevent allocations in the <see cref="Match"/> method.
        /// </summary>
        private readonly static Dictionary<UnicodeCategory, string> UnicodeCategoryStrings = new Dictionary<UnicodeCategory, string>();

        public static Key SpaceKey = new Key(" ");

        static SequenceViewModel()
        {
            foreach (var value in Enum.GetValues(typeof(UnicodeCategory)))
            {
                UnicodeCategoryStrings.Add((UnicodeCategory)value, value.ToString());
            }
        }

        public SequenceViewModel(CategoryViewModel category, SequenceDescription desc)
        {
            Category = category;
            Category.IsEmpty = false;
            Result = desc.Result; // FIXME: sequence results can be longer
            Description = desc.Description;
            Sequence = desc.Sequence;
        }

        public CategoryViewModel Category { get; private set; }

        public string Result { get; private set; }

        // TODO: verify this actually returns the Unicode of the char...
        public int Unicode { get { return Result[0]; } }

        public UnicodeCategory UnicodeCategory { get { return CharUnicodeInfo.GetUnicodeCategory(Result[0]); } }

        public string Description { get; private set; }

        public List<Key> Sequence { get; set; }

        public bool Match(SearchTokens searchText)
        {
            if (searchText.IsEmpty)
                return true;

            var compareInfo = Thread.CurrentThread.CurrentCulture.CompareInfo;
            foreach (var token in searchText.Tokens)
            {
                if (compareInfo.IndexOf(Description, token, CompareOptions.IgnoreCase) != -1)
                    return true;
            }

            foreach (var number in searchText.Numbers)
            {
                if (Unicode == number)
                    return true;
            }
            return false;
        }
    }
}
