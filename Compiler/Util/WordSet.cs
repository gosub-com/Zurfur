﻿
namespace Zurfur;

public class WordSet : WordMap<bool>
{
    public WordSet() { }
    public WordSet(string words, bool addEmptyString = false)
        : base(words, addEmptyString)
    {

    }
}
