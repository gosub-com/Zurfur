using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// Generate the ZSIL json file
    /// </summary>
    class SilGenJson
    {
        SymPackage mPackage;
        SilJsonPackage mJson = new SilJsonPackage();

        public SilGenJson(SymPackage package)
        {
            mPackage = package;
            mJson.Package = package;
        }

        public void GenerateJson()
        {
            GenerateJson(mPackage.Symbols);
        }

        void GenerateJson(Symbol topSymbol)
        {
            switch (topSymbol.Type)
            {
                case SymbolTypeEnum.Namespace:
                    foreach (var s in topSymbol.Symbols)
                        GenerateJson(s.Value);
                    break;
                case SymbolTypeEnum.Type:
                    GenerateJsonType((SymType)topSymbol);
                    break;
                case SymbolTypeEnum.Funcs:
                    GenerateJsonFuncs((SymFuncs)topSymbol);
                    break;
                case SymbolTypeEnum.Field:
                    // TBD: Must be const
                    break;
                default:
                    throw new Exception("Unexpected symbol type while generating json for: " + topSymbol.FullName);
            }
        }

        void GenerateJsonType(SymType symType)
        {
            var silType = new SilJsonType();
            silType.Name = symType.FullName;
            foreach (var s in symType.Symbols)
            {
                switch (s.Value.Type)
                {
                    case SymbolTypeEnum.Type:
                        GenerateJsonType((SymType)s.Value);
                        break;
                    case SymbolTypeEnum.Field:
                        // TBD: Field order may need to be preserved for struct
                        var symField = (SymField)s.Value;
                        silType.Fields.Add(new SilGenJsonField(symField.Name, symField.FullTypeName));
                        break;
                    case SymbolTypeEnum.Funcs:
                        break;
                    case SymbolTypeEnum.TypeArg:
                        break;
                    default:
                        throw new Exception("Unexpected symbol type while generating json for: " + symType.FullName);
                }
            }
        }


        void GenerateJsonFuncs(SymFuncs s)
        {

        }

    }
}
