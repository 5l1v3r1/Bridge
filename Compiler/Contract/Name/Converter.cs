using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bridge.Contract.Constants;
using ICSharpCode.NRefactory.TypeSystem;
using Object.Net.Utilities;

namespace Bridge.Contract
{
    public enum InitPosition
    {
        /// <summary>
        /// Emit this Method body immediately after this class defintion (default)
        /// </summary>
        After = 0,

        /// <summary>
        /// Emit this Method body Immediately before this class definition
        /// </summary>
        Before = 1,

        /// <summary>
        /// Emit the contents of this Method body directly to the Top of the file.
        /// </summary>
        Top = 2,

        /// <summary>
        /// Emit the contents of this Method body directly to the Bottom of the file.
        /// </summary>
        Bottom = 3
    }

    [Flags]
    public enum ConventionTarget
    {
        All = 0x0,
        Class = 0x1,
        Struct = 0x2,
        Enum = 0x4,
        Interface = 0x8,
        Delegate = 0x10,
        ObjectLiteral = 0x20,
        Anonymous = 0x40,
        External = 0x80,
        Member = 0x100
    }

    [Flags]
    public enum ConventionMember
    {
        All = 0x0,
        Method = 0x1,
        Property = 0x2,
        Field = 0x4,
        Event = 0x8,
        Const = 0x10,
        EnumItem = 0x20
    }

    [Flags]
    public enum ConventionAccessibility
    {
        All = 0x0,
        Public = 0x1,
        Protected = 0x2,
        Private = 0x4,
        Internal = 0x8,
        ProtectedInternal = 0x10
    }

    public enum Notation
    {
        None = 0,
        LowerCase = 1,
        UpperCase = 2,
        CamelCase = 3,
        PascalCase = 4
    }

    public enum NameRuleLevel
    {
        None,
        Assembly,
        Class,
        Member
    }

    public class NameRule
    {
        public Notation Notation
        {
            get; set;
        }

        public ConventionTarget Target
        {
            get; set;
        }

        public ConventionMember Member
        {
            get; set;
        }

        public ConventionAccessibility Accessibility
        {
            get; set;
        }

        public string Filter
        {
            get; set;
        }

        public string CustomName
        {
            get; set;
        }

        public int Priority
        {
            get; set;
        }

        public NameRuleLevel Level
        {
            get; set;
        }
    }

    public static class NameConvertor
    {

        private static readonly List<NameRule> defaultRules = new List<NameRule>
        {
            //new NameRule { Member = NotationMember.Method, Notation = Notation.CamelCase},
            //new NameRule { Member = NotationMember.Field, Notation = Notation.CamelCase}
        };

        public static string Convert(NameSemantic semantic)
        {
            var rules = NameConvertor.GetRules(semantic);
            string customName = null;
            foreach (var rule in rules)
            {
                if (NameConvertor.IsRuleAcceptable(semantic, rule))
                {
                    if (!string.IsNullOrWhiteSpace(rule.CustomName))
                    {
                        customName = rule.CustomName;
                        continue;
                    }
                    return NameConvertor.ApplyRule(semantic, rule, customName);
                }
            }

            return NameConvertor.ApplyRule(semantic, null, customName);
        }

        private static string ApplyRule(NameSemantic semantic, NameRule rule, string customName)
        {
            semantic.AppliedRule = rule;
            var name = semantic.DefaultName;

            if (rule != null)
            {
                if (!string.IsNullOrWhiteSpace(rule.CustomName))
                {
                    customName = rule.CustomName;
                }

                switch (rule.Notation)
                {
                    case Notation.None:
                        break;
                    case Notation.UpperCase:
                        name = name.ToUpperInvariant();
                        break;
                    case Notation.LowerCase:
                        name = name.ToLowerInvariant();
                        break;
                    case Notation.CamelCase:
                        var rejectRule = rule.Level != NameRuleLevel.Member && semantic.Entity is IMember &&
                                         !semantic.IsCustomName &&
                                         semantic.Entity.Name.Length > 1 &&
                                         semantic.Entity.Name.ToUpperInvariant() == semantic.Entity.Name;
                        if (rejectRule)
                        {
                            int upperCount = 0;
                            for (int i = 0; i < semantic.Entity.Name.Length; i++)
                            {
                                if (char.IsUpper(semantic.Entity.Name[i])) upperCount++;
                                if (char.IsLower(semantic.Entity.Name[i]))
                                {
                                    rejectRule = false;
                                    break;
                                }
                            }

                            if (upperCount <= 1)
                            {
                                rejectRule = false;
                            }
                        }

                        if (!rejectRule)
                        {
                            name = name.ToLowerCamelCase();
                        }

                        break;
                    case Notation.PascalCase:
                        name = name.ToCamelCase();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(rule.Notation), rule.Notation, null);
                }
            }

            if (!string.IsNullOrWhiteSpace(customName))
            {
                name = Helpers.ConvertNameTokens(customName, name);
            }

            if (semantic.Entity is IMember)
            {
                bool isIgnore = semantic.Entity.DeclaringTypeDefinition != null && semantic.Entity.DeclaringTypeDefinition.IsExternal();
                if (!isIgnore && semantic.Entity.IsStatic && Helpers.IsReservedStaticName(name, false))
                {
                    name = Helpers.ChangeReservedWord(name);
                }
            }

            return name;
        }

        private static bool IsRuleAcceptable(NameSemantic semantic, NameRule rule)
        {
            var acceptable = true;
            var entity = semantic.Entity;

            if (rule.Target != ConventionTarget.All)
            {
                var typeDef = entity as ITypeDefinition;
                if (typeDef == null && rule.Target.HasFlag(ConventionTarget.External))
                {
                    acceptable = entity.IsExternal() ||entity.IsVirtual();

                    if (!acceptable)
                    {
                        typeDef = entity.DeclaringTypeDefinition;
                    }
                }
                else if (rule.Target.HasFlag(ConventionTarget.Member) && !(entity is IMember))
                {
                    acceptable = false;
                }
                else if (rule.Target.HasFlag(ConventionTarget.Anonymous))
                {
                    if (entity is IMember && (entity.DeclaringType == null || entity.DeclaringType.Kind != TypeKind.Anonymous))
                    {
                        acceptable = false;
                    }
                    else if (entity is IType && ((IType)entity).Kind != TypeKind.Anonymous)
                    {
                        acceptable = false;
                    }
                }
                else if (typeDef == null && rule.Member != ConventionMember.All)
                {
                    typeDef = entity.DeclaringTypeDefinition;
                }

                if (typeDef != null)
                {
                    foreach (var notationType in GetFlags(rule.Target))
                    {
                        if (notationType == ConventionTarget.Member)
                        {
                            continue;
                        }

                        acceptable = NameConvertor.IsAcceptableTarget(semantic, notationType, typeDef);
                        if (acceptable)
                        {
                            break;
                        }
                    }
                }
                else if (acceptable && !rule.Target.HasFlag(ConventionTarget.Member) && !rule.Target.HasFlag(ConventionTarget.External) && !rule.Target.HasFlag(ConventionTarget.Anonymous))
                {
                    acceptable = false;
                }

                if (!acceptable)
                {
                    return false;
                }
            }

            if (rule.Accessibility != ConventionAccessibility.All)
            {
                if (!(rule.Accessibility.HasFlag(ConventionAccessibility.Public) && entity.IsPublic ||
                    rule.Accessibility.HasFlag(ConventionAccessibility.Protected) && entity.IsProtected ||
                    rule.Accessibility.HasFlag(ConventionAccessibility.ProtectedInternal) && entity.IsProtectedOrInternal ||
                    rule.Accessibility.HasFlag(ConventionAccessibility.Private) && entity.IsPrivate ||
                    rule.Accessibility.HasFlag(ConventionAccessibility.Internal) && entity.IsInternal))
                {
                    acceptable = false;
                }

                if (!acceptable)
                {
                    return false;
                }
            }

            if (rule.Member != ConventionMember.All)
            {
                var field = entity as IField;
                if (field != null)
                {
                    if (!(rule.Member.HasFlag(ConventionMember.Field) && !field.IsConst ||
                          rule.Member.HasFlag(ConventionMember.EnumItem) && field.IsConst && semantic.Entity.DeclaringTypeDefinition.Kind == TypeKind.Enum ||
                          rule.Member.HasFlag(ConventionMember.Const) && field.IsConst && semantic.Entity.DeclaringTypeDefinition.Kind != TypeKind.Enum))
                    {
                        acceptable = false;
                    }
                }
                else if (!(rule.Member.HasFlag(ConventionMember.Event) && entity is IEvent ||
                    rule.Member.HasFlag(ConventionMember.Method) && entity is IMethod ||
                    rule.Member.HasFlag(ConventionMember.Property) && entity is IProperty))
                {
                    acceptable = false;
                }

                if (!acceptable)
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(rule.Filter))
            {
                var fullName = entity.FullName;
                var parts = rule.Filter.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                acceptable = false;

                foreach (var part in parts)
                {
                    string pattern;
                    bool exclude = part.StartsWith("!");

                    if (part.StartsWith("regex:"))
                    {
                        pattern = part.Substring(6);
                    }
                    else
                    {
                        pattern = "^" + Regex.Escape(part).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    }

                    if (Regex.IsMatch(fullName, pattern))
                    {
                        acceptable = !exclude;
                    }
                }

                if (!acceptable)
                {
                    return false;
                }
            }

            return acceptable;
        }

        private static IEnumerable<T> GetFlags<T>(this T value) where T : struct
        {
            foreach (T flag in Enum.GetValues(typeof(T)).Cast<T>())
            {
                if (value.IsFlagSet(flag))
                    yield return flag;
            }
        }

        private static bool IsFlagSet<T>(this T value, T flag) where T : struct
        {
            long lValue = System.Convert.ToInt64(value);
            long lFlag = System.Convert.ToInt64(flag);
            return (lValue & lFlag) != 0;
        }

        private static bool IsAcceptableTarget(NameSemantic semantic, ConventionTarget target, ITypeDefinition typeDef)
        {
            bool acceptable = true;
            switch (target)
            {
                case ConventionTarget.Class:
                    acceptable = typeDef.Kind == TypeKind.Class;
                    break;
                case ConventionTarget.Struct:
                    acceptable = typeDef.Kind == TypeKind.Struct;
                    break;
                case ConventionTarget.Enum:
                    acceptable = typeDef.Kind == TypeKind.Enum;
                    break;
                case ConventionTarget.Interface:
                    acceptable = typeDef.Kind == TypeKind.Interface;
                    break;
                case ConventionTarget.Delegate:
                    acceptable = typeDef.Kind == TypeKind.Delegate;
                    break;
                case ConventionTarget.ObjectLiteral:
                    acceptable = semantic.IsObjectLiteral || typeDef.IsObjectLiteral();
                    break;
                case ConventionTarget.Anonymous:
                    acceptable = typeDef.Kind == TypeKind.Anonymous;
                    break;
                case ConventionTarget.External:
                    var has = typeDef.IsExternal();
                    if (!has && typeDef.DeclaringTypeDefinition != null)
                    {
                        has = typeDef.DeclaringTypeDefinition.IsExternal();
                    }

                    if (!has)
                    {
                        has = typeDef.ParentAssembly.IsExternal();
                    }
                    acceptable = has;
                    break;
                default:
                    break;
            }
            return acceptable;
        }
        private static readonly NameRule ConstructorRule = new NameRule { CustomName = JS.Funcs.CONSTRUCTOR, Level = NameRuleLevel.Member };
        private static readonly NameRule LowerCamelCaseRule = new NameRule {Notation = Notation.CamelCase, Level = NameRuleLevel.Assembly };
        private static readonly NameRule LowerCaseRule = new NameRule { Notation = Notation.LowerCase, Level = NameRuleLevel.Assembly };
        private static readonly NameRule UpperCaseRule = new NameRule { Notation = Notation.UpperCase, Level = NameRuleLevel.Assembly };
        private static readonly NameRule DefaultCaseRule = new NameRule { Notation = Notation.None, Level = NameRuleLevel.Assembly };

        private static List<NameRule> GetRules(NameSemantic semantic)
        {
            var rules = NameConvertor.GetSpecialRules(semantic);
            rules.AddRange(NameConvertor.GetAttributeRules(semantic));
            rules.AddRange(NameConvertor.GetDefaultRules(semantic));

            return rules;
        }

        private static List<NameRule> GetDefaultRules(NameSemantic semantic)
        {
            var rules = new List<NameRule>();

            var enumRule = NameConvertor.GetEnumRule(semantic);
            if (enumRule != null)
            {
                rules.Add(enumRule);
            }

            var propRule = NameConvertor.GetPropertyRule(semantic);
            if (propRule != null)
            {
                rules.Add(propRule);
            }

            rules.AddRange(NameConvertor.defaultRules);

            return rules;
        }

        private static NameRule GetEnumRule(NameSemantic semantic)
        {
            int enumMode = -1;
            if (semantic.Entity is IField && semantic.Entity.DeclaringType.Kind == TypeKind.Enum)
            {
                enumMode = semantic.Entity.DeclaringType.GetDefinition().EnumEmitMode();
                semantic.EnumMode = enumMode;
            }

            switch (enumMode)
            {
                case 1:
                    if (semantic.Entity.Name.Length > 1 &&
                        semantic.Entity.Name.ToUpperInvariant() == semantic.Entity.Name)
                    {
                        return NameConvertor.DefaultCaseRule;
                    }
                    return NameConvertor.LowerCamelCaseRule;
                case 3:
                    return NameConvertor.LowerCamelCaseRule;
                case 2:
                case 4:
                case 7:
                    return NameConvertor.DefaultCaseRule;
                case 5:
                case 8:
                    return NameConvertor.LowerCaseRule;
                case 6:
                case 9:
                    return NameConvertor.UpperCaseRule;
            }

            return null;
        }

        private static NameRule GetPropertyRule(NameSemantic semantic)
        {
            /*if ((semantic.Entity is IProperty || semantic.Entity is IField) && semantic.Entity.DeclaringTypeDefinition != null && (semantic.IsObjectLiteral || semantic.Emitter.Validator.IsObjectLiteral(semantic.Entity.DeclaringTypeDefinition)))
            {
                return NameConvertor.LowerCamelCaseRule;
            }*/

            return null;
        }

        private static List<NameRule> GetSpecialRules(NameSemantic semantic)
        {
            var rules = new List<NameRule>();

            var rule = semantic.Entity.GetNameAttributeAsRule();
            if (rule != null)
            {
                semantic.IsCustomName = true;
                rules.Add(rule);
            }
            else
            {
                var method = semantic.Entity as IMethod;
                if (method != null && method.IsConstructor)
                {
                    semantic.IsCustomName = true;
                    rules.Add(NameConvertor.ConstructorRule);
                }
            }

            return rules;
        }


        private static List<NameRule> GetAttributeRules(NameSemantic semantic)
        {
            NameRule memberRule = null;
            NameRule[] classRules = null;
            NameRule[] assemblyRules = null;
            NameRule[] interfaceRules = null;

            if (semantic.Entity is IMember)
            {
                memberRule = semantic.Entity.GetConvention(NameRuleLevel.Member);
                var typeDef = semantic.Entity.DeclaringTypeDefinition;

                if (typeDef != null)
                {
                    classRules = NameConvertor.GetClassRules(semantic, typeDef);
                }

                interfaceRules = NameConvertor.GetVirtualMemberRules(semantic);
            }
            else if (semantic.Entity is ITypeDefinition)
            {
                classRules = NameConvertor.GetClassRules(semantic, (ITypeDefinition)semantic.Entity);
            }

            var assembly = semantic.Entity.ParentAssembly;

            if (semantic.Emitter.AssemblyNameRuleCache.ContainsKey(assembly))
            {
                assemblyRules = semantic.Emitter.AssemblyNameRuleCache[assembly];
            }
            else
            {
                assemblyRules = assembly.GetConventions(NameRuleLevel.Assembly).ToArray();

                Array.Sort(assemblyRules, (item1, item2) => -item1.Priority.CompareTo(item2.Priority));

                semantic.Emitter.AssemblyNameRuleCache.Add(assembly, assemblyRules);
            }

            var rules = new List<NameRule>();

            if (memberRule != null)
            {
                rules.Add(memberRule);
            }

            if (classRules != null && classRules.Length > 0)
            {
                rules.AddRange(classRules);
            }

            if (interfaceRules != null && interfaceRules.Length > 0)
            {
                rules.AddRange(interfaceRules);
            }

            if (assemblyRules != null && assemblyRules.Length > 0)
            {
                rules.AddRange(assemblyRules);
            }

            return rules;
        }

        private static NameRule[] GetVirtualMemberRules(NameSemantic semantic)
        {
            var member = semantic.Entity as IMember;

            if (member != null)
            {
                if (member.IsOverride)
                {
                    var baseMember = InheritanceHelper.GetBaseMember(member);
                    var baseSemantic = NameSemantic.Create(baseMember, semantic.Emitter);
                    //do not remove baseName, it calculates AppliedRule
                    var baseName = baseSemantic.Name;
                    if (baseSemantic.AppliedRule != null)
                    {
                        return new[] { baseSemantic.AppliedRule };
                    }
                }

                if (member.ImplementedInterfaceMembers.Count > 0)
                {
                    var interfaceMember = member.ImplementedInterfaceMembers.First();
                    return NameConvertor.GetClassRules(new NameSemantic { Emitter = semantic.Emitter }, interfaceMember.DeclaringTypeDefinition);
                }
            }

            return null;
        }

        private static NameRule[] GetClassRules(NameSemantic semantic, ITypeDefinition typeDef)
        {
            if (semantic.Emitter.ClassNameRuleCache.ContainsKey(typeDef))
            {
                return semantic.Emitter.ClassNameRuleCache[typeDef];
            }

            var td = typeDef;
            var rules = new List<NameRule>();
            while (td != null)
            {
                rules.AddRange(td.GetConventions(NameRuleLevel.Class));
                td = td.DeclaringTypeDefinition;
            }

            var classRules = rules.ToArray();
            semantic.Emitter.ClassNameRuleCache.Add(typeDef, classRules);
            return classRules;
        }
    }
}