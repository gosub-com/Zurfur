
namespace Gosub.Zurfur.Lex
{
    class WordSet : WordMap<bool>
    {
        public WordSet() { }
        public WordSet(string words, bool addEmptyString = false)
            : base(words, addEmptyString)
        {

        }
    }
}
