
namespace Gosub.Zurfur.Compiler
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
