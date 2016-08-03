// The MIT License (MIT)

// Copyright 2015 Siney/Pangweiwei siney@yeah.net
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
namespace SLua
{
    using LuaInterface;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using System.Text.RegularExpressions;

    public class LuaCodeGen
    {
        static public string GenPath = "../../";

        public delegate void ExportGenericDelegate(Type t, string ns);

        static public void GenerateAll()
        {
            Custom();
        }

        static public void Custom()
        {
            List<Type> exports = new List<Type>();
            string path = GenPath + "/Server/Server/LuaExport/";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            ExportGenericDelegate fun = (Type t, string ns) =>
            {
                if (Generate(t, ns, path))
                    exports.Add(t);
            };

            Assembly assembly;
            Type[] types;
            // export self-dll
            assembly = Assembly.GetExecutingAssembly();
            types = assembly.GetExportedTypes();
            foreach (Type t in types)
            {
                if (t.IsDefined(typeof(CustomLuaClassAttribute), false))
                {
                    fun(t, null);
                    Debug.LogFormat("{0} exported...", t.FullName);
                }
            }
            GenerateBind(exports, "BindCustom", 3, path);
        }

        private static bool Generate(Type t, string ns, string path)
        {
            if (t.IsInterface)
                return false;

            CodeGenerator cg = new CodeGenerator();
            cg.givenNamespace = ns;
            cg.path = path;
            return cg.Generate(t);
        }

        private static void GenerateBind(List<Type> list, string name, int order, string path)
        {
            CodeGenerator cg = new CodeGenerator();
            cg.path = path;
            cg.GenerateBind(list, name, order);
        }
    }

    internal class CodeGenerator
    {
        private static List<string> memberFilter = new List<string>
        {
            "AnimationClip.averageDuration"
        };

        public static void OnGetAssemblyToGenerateExtensionMethod(out List<string> list)
        {
            list = new List<string> {
                "",
            };
        }

        private static Dictionary<System.Type, List<MethodInfo>> GenerateExtensionMethodsMap()
        {
            Dictionary<Type, List<MethodInfo>> dic = new Dictionary<Type, List<MethodInfo>>();
            List<string> asems;
            // OnGetAssemblyToGenerateExtensionMethod(out asems);
            // foreach (string assstr in asems)
            // {
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach (Type type in assembly.GetExportedTypes())
            {
                if (type.IsSealed && !type.IsGenericType && !type.IsNested)
                {
                    MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public);
                    foreach (MethodInfo method in methods)
                    {
                        if (IsExtensionMethod(method))
                        {
                            Type extendedType = method.GetParameters()[0].ParameterType;
                            if (!dic.ContainsKey(extendedType))
                            {
                                dic.Add(extendedType, new List<MethodInfo>());
                            }
                            dic[extendedType].Add(method);
                        }
                    }
                }
            }
            // }
            return dic;
        }

        private static bool IsExtensionMethod(MethodBase method)
        {
            return method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);
        }

        private static Dictionary<System.Type, List<MethodInfo>> extensionMethods = new Dictionary<Type, List<MethodInfo>>();

        static CodeGenerator()
        {
            extensionMethods = GenerateExtensionMethodsMap();
        }

        private HashSet<string> funcname = new HashSet<string>();
        private Dictionary<string, bool> directfunc = new Dictionary<string, bool>();

        public string givenNamespace;
        public string path;

        //  public bool includeExtension = SLuaSetting.Instance.exportExtensionMethod;
        public EOL eol = EOL.Native;

        private class PropPair
        {
            public string get = "null";
            public string set = "null";
            public bool isInstance = true;
        }

        private Dictionary<string, PropPair> propname = new Dictionary<string, PropPair>();

        private int indent = 0;

        public void GenerateBind(List<Type> list, string name, int order)
        {
            HashSet<Type> exported = new HashSet<Type>();
            string f = System.IO.Path.Combine(path, name + ".cs");//ÎÄ¼þÂ·¾¶;
            StreamWriter file = new StreamWriter(f, false, Encoding.UTF8);
            file.NewLine = NewLine;
            Write(file, "using System;");
            Write(file, "using System.Collections.Generic;");
            Write(file, "namespace SLua {");
            Write(file, "[LuaBinder({0})]", order);
            Write(file, "public class {0} {{", name);
            Write(file, "public static Action<IntPtr>[] GetBindList() {");
            Write(file, "Action<IntPtr>[] list= {");
            foreach (Type t in list)
            {
                WriteBindType(file, t, list, exported);
            }
            Write(file, "};");
            Write(file, "return list;");
            Write(file, "}");
            Write(file, "}");
            Write(file, "}");
            file.Close();
        }

        private void WriteBindType(StreamWriter file, Type t, List<Type> exported, HashSet<Type> binded)
        {
            if (t == null || binded.Contains(t) || !exported.Contains(t))
                return;
            WriteBindType(file, t.BaseType, exported, binded);
            Write(file, "{0}.reg,", ExportName(t), binded);
            binded.Add(t);
        }

        public bool Generate(Type t)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            if (!t.IsGenericTypeDefinition
                || (t.BaseType != null && t.BaseType == typeof(System.MulticastDelegate)))
            {
                if (t.IsEnum)
                {
                    StreamWriter file = Begin(t);
                    WriteHead(t, file);
                    RegEnumFunction(t, file);
                    End(file);
                }
                else if (t.BaseType == typeof(System.MulticastDelegate))
                {
                    string f;
                    if (t.IsGenericType)
                    {
                        if (t.ContainsGenericParameters)
                            return false;

                        f = path + string.Format("Lua{0}_{1}.cs", _Name(GenericBaseName(t)), _Name(GenericName(t)));
                    }
                    else
                    {
                        f = path + "LuaDelegate_" + _Name(t.FullName) + ".cs";
                    }
                    StreamWriter file = new StreamWriter(f, false, Encoding.UTF8);
                    file.NewLine = NewLine;
                    WriteDelegate(t, file);
                    file.Close();
                    return false;
                }
                else
                {
                    funcname.Clear();
                    propname.Clear();
                    directfunc.Clear();

                    StreamWriter file = Begin(t);
                    WriteHead(t, file);
                    WriteConstructor(t, file);
                    WriteFunction(t, file, false);
                    WriteFunction(t, file, true);
                    WriteField(t, file);
                    RegFunction(t, file);
                    End(file);
                }

                return true;
            }
            return false;
        }

        private void WriteDelegate(Type t, StreamWriter file)
        {
            string temp = @"
using System;
using System.Collections.Generic;
using LuaInterface;

namespace SLua
{
    public partial class LuaDelegation : LuaObject
    {
        static internal int checkDelegate(IntPtr l,int p,out $FN ua) {
            int op = extractFunction(l,p);
			if(LuaDLL.lua_isnil(l,p)) {
				ua=null;
				return op;
			}
            else if (LuaDLL.lua_isuserdata(l, p)==1)
            {
                ua = ($FN)checkObj(l, p);
                return op;
            }
            LuaDelegate ld;
            checkType(l, -1, out ld);
            if(ld.d!=null)
            {
                ua = ($FN)ld.d;
                return op;
            }
			LuaDLL.lua_pop(l,1);

			l = LuaState.get(l).L;
            ua = ($ARGS) =>
            {
                int error = pushTry(l);
";

            temp = temp.Replace("$TN", t.Name);
            temp = temp.Replace("$FN", SimpleType(t));
            MethodInfo mi = t.GetMethod("Invoke");
            List<int> outindex = new List<int>();
            List<int> refindex = new List<int>();
            temp = temp.Replace("$ARGS", ArgsList(mi, ref outindex, ref refindex));
            Write(file, temp);

            this.indent = 4;

            for (int n = 0; n < mi.GetParameters().Length; n++)
            {
                if (!outindex.Contains(n))
                    Write(file, "pushValue(l,a{0});", n + 1);
            }

            Write(file, "ld.pcall({0}, error);", mi.GetParameters().Length - outindex.Count);

            int offset = 0;
            if (mi.ReturnType != typeof(void))
            {
                offset = 1;
                WriteValueCheck(file, mi.ReturnType, offset, "ret", "error+");
            }

            foreach (int i in outindex)
            {
                string a = string.Format("a{0}", i + 1);
                WriteCheckType(file, mi.GetParameters()[i].ParameterType, i + offset, a, "error+");
            }

            foreach (int i in refindex)
            {
                string a = string.Format("a{0}", i + 1);
                WriteCheckType(file, mi.GetParameters()[i].ParameterType, i + offset, a, "error+");
            }

            Write(file, "LuaDLL.lua_settop(l, error-1);");
            if (mi.ReturnType != typeof(void))
                Write(file, "return ret;");

            Write(file, "};");
            Write(file, "ld.d=ua;");
            Write(file, "return op;");
            Write(file, "}");
            Write(file, "}");
            Write(file, "}");
        }

        private string ArgsList(MethodInfo m, ref List<int> outindex, ref List<int> refindex)
        {
            string str = "";
            ParameterInfo[] pars = m.GetParameters();
            for (int n = 0; n < pars.Length; n++)
            {
                string t = SimpleType(pars[n].ParameterType);

                ParameterInfo p = pars[n];
                if (p.ParameterType.IsByRef && p.IsOut)
                {
                    str += string.Format("out {0} a{1}", t, n + 1);
                    outindex.Add(n);
                }
                else if (p.ParameterType.IsByRef)
                {
                    str += string.Format("ref {0} a{1}", t, n + 1);
                    refindex.Add(n);
                }
                else
                    str += string.Format("{0} a{1}", t, n + 1);
                if (n < pars.Length - 1)
                    str += ",";
            }
            return str;
        }

        private void tryMake(Type t)
        {
            if (t.BaseType == typeof(System.MulticastDelegate))
            {
                CodeGenerator cg = new CodeGenerator();
                cg.path = this.path;
                cg.Generate(t);
            }
        }

        private void RegEnumFunction(Type t, StreamWriter file)
        {
            // Write export function
            Write(file, "static public void reg(IntPtr l) {");
            Write(file, "getEnumTable(l,\"{0}\");", string.IsNullOrEmpty(givenNamespace) ? FullName(t) : givenNamespace);

            foreach (object value in Enum.GetValues(t))
            {
                Write(file, "addMember(l,{0},\"{1}\");", Convert.ToInt32(value), value.ToString());
            }

            Write(file, "LuaDLL.lua_pop(l, 1);");
            Write(file, "}");
        }

        private StreamWriter Begin(Type t)
        {
            string clsname = ExportName(t);
            string f = path + clsname + ".cs";
            StreamWriter file = new StreamWriter(f, false, Encoding.UTF8);
            file.NewLine = NewLine;
            return file;
        }

        private void End(StreamWriter file)
        {
            Write(file, "}");
            file.Flush();
            file.Close();
        }

        private void WriteHead(Type t, StreamWriter file)
        {
            Write(file, "using System;");
            Write(file, "using LuaInterface;");
            Write(file, "using SLua;");
            Write(file, "using System.Collections.Generic;");
            WriteExtraNamespace(file, t);
            Write(file, "public class {0} : LuaObject {{", ExportName(t));
        }

        // add namespace for extension method
        private void WriteExtraNamespace(StreamWriter file, Type t)
        {
            List<MethodInfo> lstMI;
            HashSet<string> nsset = new HashSet<string>();
            if (extensionMethods.TryGetValue(t, out lstMI))
            {
                foreach (MethodInfo m in lstMI)
                {
                    // if not writed
                    if (!string.IsNullOrEmpty(m.ReflectedType.Namespace) && !nsset.Contains(m.ReflectedType.Namespace))
                    {
                        Write(file, "using {0};", m.ReflectedType.Namespace);
                        nsset.Add(m.ReflectedType.Namespace);
                    }
                }
            }
        }

        private void WriteFunction(Type t, StreamWriter file, bool writeStatic = false)
        {
            BindingFlags bf = BindingFlags.Public | BindingFlags.DeclaredOnly;
            if (writeStatic)
                bf |= BindingFlags.Static;
            else
                bf |= BindingFlags.Instance;

            MethodInfo[] members = t.GetMethods(bf);
            List<MethodInfo> methods = new List<MethodInfo>();
            methods.AddRange(members);

            if (!writeStatic)
            {
                if (extensionMethods.ContainsKey(t))
                {
                    methods.AddRange(extensionMethods[t]);
                }
            }
            foreach (MethodInfo mi in methods)
            {
                bool instanceFunc;
                if (writeStatic && isPInvoke(mi, out instanceFunc))
                {
                    directfunc.Add(t.FullName + "." + mi.Name, instanceFunc);
                    continue;
                }

                string fn = writeStatic ? staticName(mi.Name) : mi.Name;
                if (mi.MemberType == MemberTypes.Method
                    && !IsObsolete(mi)
                    && !DontExport(mi)
                    && !funcname.Contains(fn)
                    && isUsefullMethod(mi)
                    && !MemberInFilter(t, mi))
                {
                    WriteFunctionDec(file, fn);
                    WriteFunctionImpl(file, mi, t, bf);
                    funcname.Add(fn);
                }
            }
        }

        private bool isPInvoke(MethodInfo mi, out bool instanceFunc)
        {
            if (mi.IsDefined(typeof(MonoPInvokeCallbackAttribute), false))
            {
                instanceFunc = !mi.IsDefined(typeof(StaticExportAttribute), false);
                return true;
            }
            instanceFunc = true;
            return false;
        }

        private string staticName(string name)
        {
            if (name.StartsWith("op_"))
                return name;
            return name + "_s";
        }

        private bool MemberInFilter(Type t, MemberInfo mi)
        {
            return memberFilter.Contains(t.Name + "." + mi.Name) || memberFilter.Contains("*." + mi.Name);
        }

        private bool IsObsolete(MemberInfo t)
        {
            return t.IsDefined(typeof(ObsoleteAttribute), false);
        }

        private string NewLine
        {
            get
            {
                switch (eol)
                {
                    case EOL.Native:
                        return System.Environment.NewLine;

                    case EOL.CRLF:
                        return "\r\n";

                    case EOL.CR:
                        return "\r";

                    case EOL.LF:
                        return "\n";

                    default:
                        return "";
                }
            }
        }

        private void RegFunction(Type t, StreamWriter file)
        {
            // Write export function
            Write(file, "static public void reg(IntPtr l) {");

            if (t.BaseType != null && t.BaseType.Name.Contains("UnityEvent`"))
            {
                Write(file, "LuaUnityEvent_{1}.reg(l);", FullName(t), _Name((GenericName(t.BaseType))));
            }

            Write(file, "getTypeTable(l,\"{0}\");", string.IsNullOrEmpty(givenNamespace) ? FullName(t) : givenNamespace);
            foreach (string f in funcname)
            {
                Write(file, "addMember(l,{0});", f);
            }
            foreach (string f in directfunc.Keys)
            {
                bool instance = directfunc[f];
                Write(file, "addMember(l,{0},{1});", f, instance ? "true" : "false");
            }

            foreach (string f in propname.Keys)
            {
                PropPair pp = propname[f];
                Write(file, "addMember(l,\"{0}\",{1},{2},{3});", f, pp.get, pp.set, pp.isInstance ? "true" : "false");
            }
            if (t.BaseType != null && !CutBase(t.BaseType))
            {
                if (t.BaseType.Name.Contains("UnityEvent`"))
                    Write(file, "createTypeMetatable(l,{2}, typeof({0}),typeof(LuaUnityEvent_{1}));", TypeDecl(t), _Name(GenericName(t.BaseType)), constructorOrNot(t));
                else
                    Write(file, "createTypeMetatable(l,{2}, typeof({0}),typeof({1}));", TypeDecl(t), TypeDecl(t.BaseType), constructorOrNot(t));
            }
            else
                Write(file, "createTypeMetatable(l,{1}, typeof({0}));", TypeDecl(t), constructorOrNot(t));
            Write(file, "}");
        }

        private string constructorOrNot(Type t)
        {
            ConstructorInfo[] cons = GetValidConstructor(t);
            if (cons.Length > 0 || t.IsValueType)
                return "constructor";
            return "null";
        }

        private bool CutBase(Type t)
        {
            if (t.FullName.StartsWith("System.Object"))
                return true;
            return false;
        }

        private void WriteSet(StreamWriter file, Type t, string cls, string fn, bool isstatic = false)
        {
            if (t.BaseType == typeof(MulticastDelegate))
            {
                if (isstatic)
                {
                    Write(file, "if(op==0) {0}.{1}=v;", cls, fn);
                    Write(file, "else if(op==1) {0}.{1}+=v;", cls, fn);
                    Write(file, "else if(op==2) {0}.{1}-=v;", cls, fn);
                }
                else
                {
                    Write(file, "if(op==0) self.{0}=v;", fn);
                    Write(file, "else if(op==1) self.{0}+=v;", fn);
                    Write(file, "else if(op==2) self.{0}-=v;", fn);
                }
            }
            else
            {
                if (isstatic)
                {
                    Write(file, "{0}.{1}=v;", cls, fn);
                }
                else
                {
                    Write(file, "self.{0}=v;", fn);
                }
            }
        }

        private static readonly string[] keywords = { "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while" };

        private static string NormalName(string name)
        {
            if (Array.BinarySearch<string>(keywords, name) >= 0)
            {
                return "@" + name;
            }
            return name;
        }

        private void WriteField(Type t, StreamWriter file)
        {
            // Write field set/get

            FieldInfo[] fields = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (FieldInfo fi in fields)
            {
                if (DontExport(fi) || IsObsolete(fi))
                    continue;

                PropPair pp = new PropPair();
                pp.isInstance = !fi.IsStatic;

                if (fi.FieldType.BaseType != typeof(MulticastDelegate))
                {
                    WriteFunctionAttr(file);
                    Write(file, "static public int get_{0}(IntPtr l) {{", fi.Name);
                    WriteTry(file);

                    if (fi.IsStatic)
                    {
                        WriteOk(file);
                        WritePushValue(fi.FieldType, file, string.Format("{0}.{1}", TypeDecl(t), NormalName(fi.Name)));
                    }
                    else
                    {
                        WriteCheckSelf(file, t);
                        WriteOk(file);
                        WritePushValue(fi.FieldType, file, string.Format("self.{0}", NormalName(fi.Name)));
                    }

                    Write(file, "return 2;");
                    WriteCatchExecption(file);
                    Write(file, "}");

                    pp.get = "get_" + fi.Name;
                }

                if (!fi.IsLiteral && !fi.IsInitOnly)
                {
                    WriteFunctionAttr(file);
                    Write(file, "static public int set_{0}(IntPtr l) {{", fi.Name);
                    WriteTry(file);
                    if (fi.IsStatic)
                    {
                        Write(file, "{0} v;", TypeDecl(fi.FieldType));
                        WriteCheckType(file, fi.FieldType, 2);
                        WriteSet(file, fi.FieldType, TypeDecl(t), NormalName(fi.Name), true);
                    }
                    else
                    {
                        WriteCheckSelf(file, t);
                        Write(file, "{0} v;", TypeDecl(fi.FieldType));
                        WriteCheckType(file, fi.FieldType, 2);
                        WriteSet(file, fi.FieldType, t.FullName, NormalName(fi.Name));
                    }

                    if (t.IsValueType && !fi.IsStatic)
                        Write(file, "setBack(l,self);");
                    WriteOk(file);
                    Write(file, "return 1;");
                    WriteCatchExecption(file);
                    Write(file, "}");

                    pp.set = "set_" + fi.Name;
                }

                propname.Add(fi.Name, pp);
                tryMake(fi.FieldType);
            }
            //for this[]
            List<PropertyInfo> getter = new List<PropertyInfo>();
            List<PropertyInfo> setter = new List<PropertyInfo>();
            // Write property set/get
            PropertyInfo[] props = t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (PropertyInfo fi in props)
            {
                //if (fi.Name == "Item" || IsObsolete(fi) || MemberInFilter(t,fi) || DontExport(fi))
                if (IsObsolete(fi) || MemberInFilter(t, fi) || DontExport(fi))
                    continue;
                if (fi.Name == "Item"
                    || (t.Name == "String" && fi.Name == "Chars")) // for string[]
                {
                    //for this[]
                    if (!fi.GetGetMethod().IsStatic && fi.GetIndexParameters().Length == 1)
                    {
                        if (fi.CanRead && !IsNotSupport(fi.PropertyType))
                            getter.Add(fi);
                        if (fi.CanWrite && fi.GetSetMethod() != null)
                            setter.Add(fi);
                    }
                    continue;
                }
                PropPair pp = new PropPair();
                bool isInstance = true;

                if (fi.CanRead && fi.GetGetMethod() != null)
                {
                    if (!IsNotSupport(fi.PropertyType))
                    {
                        WriteFunctionAttr(file);
                        Write(file, "static public int get_{0}(IntPtr l) {{", fi.Name);
                        WriteTry(file);

                        if (fi.GetGetMethod().IsStatic)
                        {
                            isInstance = false;
                            WriteOk(file);
                            WritePushValue(fi.PropertyType, file, string.Format("{0}.{1}", TypeDecl(t), NormalName(fi.Name)));
                        }
                        else
                        {
                            WriteCheckSelf(file, t);
                            WriteOk(file);
                            WritePushValue(fi.PropertyType, file, string.Format("self.{0}", NormalName(fi.Name)));
                        }

                        Write(file, "return 2;");
                        WriteCatchExecption(file);
                        Write(file, "}");
                        pp.get = "get_" + fi.Name;
                    }
                }

                if (fi.CanWrite && fi.GetSetMethod() != null)
                {
                    WriteFunctionAttr(file);
                    Write(file, "static public int set_{0}(IntPtr l) {{", fi.Name);
                    WriteTry(file);
                    if (fi.GetSetMethod().IsStatic)
                    {
                        WriteValueCheck(file, fi.PropertyType, 2);
                        WriteSet(file, fi.PropertyType, TypeDecl(t), NormalName(fi.Name), true);
                        isInstance = false;
                    }
                    else
                    {
                        WriteCheckSelf(file, t);
                        WriteValueCheck(file, fi.PropertyType, 2);
                        WriteSet(file, fi.PropertyType, TypeDecl(t), NormalName(fi.Name));
                    }

                    if (t.IsValueType)
                        Write(file, "setBack(l,self);");
                    WriteOk(file);
                    Write(file, "return 1;");
                    WriteCatchExecption(file);
                    Write(file, "}");
                    pp.set = "set_" + fi.Name;
                }
                pp.isInstance = isInstance;

                propname.Add(fi.Name, pp);
                tryMake(fi.PropertyType);
            }
            //for this[]
            WriteItemFunc(t, file, getter, setter);
        }

        private void WriteItemFunc(Type t, StreamWriter file, List<PropertyInfo> getter, List<PropertyInfo> setter)
        {
            //Write property this[] set/get
            if (getter.Count > 0)
            {
                //get
                bool first_get = true;
                WriteFunctionAttr(file);
                Write(file, "static public int getItem(IntPtr l) {");
                WriteTry(file);
                WriteCheckSelf(file, t);
                if (getter.Count == 1)
                {
                    PropertyInfo _get = getter[0];
                    ParameterInfo[] infos = _get.GetIndexParameters();
                    WriteValueCheck(file, infos[0].ParameterType, 2, "v");
                    Write(file, "var ret = self[v];");
                    WriteOk(file);
                    WritePushValue(_get.PropertyType, file, "ret");
                    Write(file, "return 2;");
                }
                else
                {
                    Write(file, "LuaTypes t = LuaDLL.lua_type(l, 2);");
                    for (int i = 0; i < getter.Count; i++)
                    {
                        PropertyInfo fii = getter[i];
                        ParameterInfo[] infos = fii.GetIndexParameters();
                        Write(file, "{0}(matchType(l,2,t,typeof({1}))){{", first_get ? "if" : "else if", infos[0].ParameterType);
                        WriteValueCheck(file, infos[0].ParameterType, 2, "v");
                        Write(file, "var ret = self[v];");
                        WriteOk(file);
                        WritePushValue(fii.PropertyType, file, "ret");
                        Write(file, "return 2;");
                        Write(file, "}");
                        first_get = false;
                    }
                    WriteError(file, "No matched override function to call");
                }
                WriteCatchExecption(file);
                Write(file, "}");
                funcname.Add("getItem");
            }
            if (setter.Count > 0)
            {
                bool first_set = true;
                WriteFunctionAttr(file);
                Write(file, "static public int setItem(IntPtr l) {");
                WriteTry(file);
                WriteCheckSelf(file, t);
                if (setter.Count == 1)
                {
                    PropertyInfo _set = setter[0];
                    ParameterInfo[] infos = _set.GetIndexParameters();
                    WriteValueCheck(file, infos[0].ParameterType, 2);
                    WriteValueCheck(file, _set.PropertyType, 3, "c");
                    Write(file, "self[v]=c;");
                    WriteOk(file);
                }
                else
                {
                    Write(file, "LuaTypes t = LuaDLL.lua_type(l, 2);");
                    for (int i = 0; i < setter.Count; i++)
                    {
                        PropertyInfo fii = setter[i];
                        if (t.BaseType != typeof(MulticastDelegate))
                        {
                            ParameterInfo[] infos = fii.GetIndexParameters();
                            Write(file, "{0}(matchType(l,2,t,typeof({1}))){{", first_set ? "if" : "else if", infos[0].ParameterType);
                            WriteValueCheck(file, infos[0].ParameterType, 2, "v");
                            WriteValueCheck(file, fii.PropertyType, 3, "c");
                            Write(file, "self[v]=c;");
                            WriteOk(file);
                            Write(file, "return 1;");
                            Write(file, "}");
                            first_set = false;
                        }
                        if (t.IsValueType)
                            Write(file, "setBack(l,self);");
                    }
                    Write(file, "LuaDLL.lua_pushstring(l,\"No matched override function to call\");");
                }
                Write(file, "return 1;");
                WriteCatchExecption(file);
                Write(file, "}");
                funcname.Add("setItem");
            }
        }

        private void WriteTry(StreamWriter file)
        {
            Write(file, "try {");
        }

        private void WriteCatchExecption(StreamWriter file)
        {
            Write(file, "}");
            Write(file, "catch(Exception e) {");
            Write(file, "return error(l,e);");
            Write(file, "}");
        }

        private void WriteCheckType(StreamWriter file, Type t, int n, string v = "v", string nprefix = "")
        {
            if (t.IsEnum)
                Write(file, "checkEnum(l,{2}{0},out {1});", n, v, nprefix);
            else if (t.BaseType == typeof(System.MulticastDelegate))
                Write(file, "int op=LuaDelegation.checkDelegate(l,{2}{0},out {1});", n, v, nprefix);
            else if (IsValueType(t))
                Write(file, "checkValueType(l,{2}{0},out {1});", n, v, nprefix);
            else
                Write(file, "checkType(l,{2}{0},out {1});", n, v, nprefix);
        }

        private void WriteValueCheck(StreamWriter file, Type t, int n, string v = "v", string nprefix = "")
        {
            Write(file, "{0} {1};", SimpleType(t), v);
            WriteCheckType(file, t, n, v, nprefix);
        }

        private void WriteFunctionAttr(StreamWriter file)
        {
            Write(file, "[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]");
        }

        private ConstructorInfo[] GetValidConstructor(Type t)
        {
            List<ConstructorInfo> ret = new List<ConstructorInfo>();
            if (t.GetConstructor(Type.EmptyTypes) == null && t.IsAbstract && t.IsSealed)
                return ret.ToArray();
            if (t.IsAbstract)
                return ret.ToArray();
            if (t.BaseType != null && t.BaseType.Name == "MonoBehaviour")
                return ret.ToArray();

            ConstructorInfo[] cons = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
            foreach (ConstructorInfo ci in cons)
            {
                if (!IsObsolete(ci) && !DontExport(ci) && !ContainUnsafe(ci))
                    ret.Add(ci);
            }
            return ret.ToArray();
        }

        private bool ContainUnsafe(MethodBase mi)
        {
            foreach (ParameterInfo p in mi.GetParameters())
            {
                if (p.ParameterType.FullName.Contains("*"))
                    return true;
            }
            return false;
        }

        private bool DontExport(MemberInfo mi)
        {
            var methodString = string.Format("{0}.{1}", mi.DeclaringType, mi.Name);

            return mi.IsDefined(typeof(DoNotToLuaAttribute), false);
        }

        private void WriteConstructor(Type t, StreamWriter file)
        {
            ConstructorInfo[] cons = GetValidConstructor(t);
            if (cons.Length > 0)
            {
                WriteFunctionAttr(file);
                Write(file, "static public int constructor(IntPtr l) {");
                WriteTry(file);
                if (cons.Length > 1)
                    Write(file, "int argc = LuaDLL.lua_gettop(l);");
                Write(file, "{0} o;", TypeDecl(t));
                bool first = true;
                for (int n = 0; n < cons.Length; n++)
                {
                    ConstructorInfo ci = cons[n];
                    ParameterInfo[] pars = ci.GetParameters();

                    if (cons.Length > 1)
                    {
                        if (isUniqueArgsCount(cons, ci))
                            Write(file, "{0}(argc=={1}){{", first ? "if" : "else if", ci.GetParameters().Length + 1);
                        else
                            Write(file, "{0}(matchType(l,argc,2{1})){{", first ? "if" : "else if", TypeDecl(pars));
                    }

                    for (int k = 0; k < pars.Length; k++)
                    {
                        ParameterInfo p = pars[k];
                        bool hasParams = p.IsDefined(typeof(ParamArrayAttribute), false);
                        CheckArgument(file, p.ParameterType, k, 2, p.IsOut, hasParams);
                    }
                    Write(file, "o=new {0}({1});", TypeDecl(t), FuncCall(ci));
                    WriteOk(file);
                    if (t.Name == "String") // if export system.string, push string as ud not lua string
                        Write(file, "pushObject(l,o);");
                    else
                        Write(file, "pushValue(l,o);");
                    Write(file, "return 2;");
                    if (cons.Length == 1)
                        WriteCatchExecption(file);
                    Write(file, "}");
                    first = false;
                }

                if (cons.Length > 1)
                {
                    Write(file, "return error(l,\"New object failed.\");");
                    WriteCatchExecption(file);
                    Write(file, "}");
                }
            }
            else if (t.IsValueType) // default constructor
            {
                WriteFunctionAttr(file);
                Write(file, "static public int constructor(IntPtr l) {");
                WriteTry(file);
                Write(file, "{0} o;", FullName(t));
                Write(file, "o=new {0}();", FullName(t));
                WriteReturn(file, "o");
                WriteCatchExecption(file);
                Write(file, "}");
            }
        }

        private void WriteOk(StreamWriter file)
        {
            Write(file, "pushValue(l,true);");
        }

        private void WriteBad(StreamWriter file)
        {
            Write(file, "pushValue(l,false);");
        }

        private void WriteError(StreamWriter file, string err)
        {
            WriteBad(file);
            Write(file, "LuaDLL.lua_pushstring(l,\"{0}\");", err);
            Write(file, "return 2;");
        }

        private void WriteReturn(StreamWriter file, string val)
        {
            Write(file, "pushValue(l,true);");
            Write(file, "pushValue(l,{0});", val);
            Write(file, "return 2;");
        }

        private bool IsNotSupport(Type t)
        {
            if (t.IsSubclassOf(typeof(Delegate)))
                return true;
            return false;
        }

        private string[] prefix = new string[] { "System.Collections.Generic" };

        private string RemoveRef(string s, bool removearray = true)
        {
            if (s.EndsWith("&")) s = s.Substring(0, s.Length - 1);
            if (s.EndsWith("[]") && removearray) s = s.Substring(0, s.Length - 2);
            if (s.StartsWith(prefix[0])) s = s.Substring(prefix[0].Length + 1, s.Length - prefix[0].Length - 1);

            s = s.Replace("+", ".");
            if (s.Contains("`"))
            {
                string regstr = @"`\d";
                Regex r = new Regex(regstr, RegexOptions.None);
                s = r.Replace(s, "");
                s = s.Replace("[", "<");
                s = s.Replace("]", ">");
            }
            return s;
        }

        private string GenericBaseName(Type t)
        {
            string n = t.FullName;
            if (n.IndexOf('[') > 0)
            {
                n = n.Substring(0, n.IndexOf('['));
            }
            return n.Replace("+", ".");
        }

        private string GenericName(Type t, string sep = "_")
        {
            try
            {
                Type[] tt = t.GetGenericArguments();
                string ret = "";
                for (int n = 0; n < tt.Length; n++)
                {
                    string dt = SimpleType(tt[n]);
                    ret += dt;
                    if (n < tt.Length - 1)
                        ret += sep;
                }
                return ret;
            }
            catch (Exception e)
            {
                Debug.Log(e.ToString());
                return "";
            }
        }

        private string _Name(string n)
        {
            string ret = "";
            for (int i = 0; i < n.Length; i++)
            {
                if (char.IsLetterOrDigit(n[i]))
                    ret += n[i];
                else
                    ret += "_";
            }
            return ret;
        }

        private string TypeDecl(ParameterInfo[] pars, int paraOffset = 0)
        {
            string ret = "";
            for (int n = paraOffset; n < pars.Length; n++)
            {
                ret += ",typeof(";
                if (pars[n].IsOut)
                    ret += "LuaOut";
                else
                    ret += SimpleType(pars[n].ParameterType);
                ret += ")";
            }
            return ret;
        }

        private bool isUsefullMethod(MethodInfo method)
        {
            if (method.Name != "GetType" && method.Name != "GetHashCode" && method.Name != "Equals" &&
                method.Name != "ToString" && method.Name != "Clone" &&
                method.Name != "GetEnumerator" && method.Name != "CopyTo" &&
                method.Name != "op_Implicit" && method.Name != "op_Explicit" &&
                !method.Name.StartsWith("get_", StringComparison.Ordinal) &&
                !method.Name.StartsWith("set_", StringComparison.Ordinal) &&
                !method.Name.StartsWith("add_", StringComparison.Ordinal) &&
                !IsObsolete(method) && !method.IsGenericMethod &&
                method.ToString() != "Int32 Clamp(Int32, Int32, Int32)" &&
                !method.Name.StartsWith("remove_", StringComparison.Ordinal))
            {
                return true;
            }
            return false;
        }

        private void WriteFunctionDec(StreamWriter file, string name)
        {
            WriteFunctionAttr(file);
            Write(file, "static public int {0}(IntPtr l) {{", name);
        }

        private MethodBase[] GetMethods(Type t, string name, BindingFlags bf)
        {
            List<MethodBase> methods = new List<MethodBase>();

            if (((bf & BindingFlags.Instance) == BindingFlags.Instance))
            {
                if (extensionMethods.ContainsKey(t))
                {
                    foreach (MethodInfo m in extensionMethods[t])
                    {
                        if (m.Name == name
                           && !IsObsolete(m)
                           && !DontExport(m)
                           && isUsefullMethod(m))
                        {
                            methods.Add(m);
                        }
                    }
                }
            }

            MemberInfo[] cons = t.GetMember(name, bf);
            foreach (MemberInfo m in cons)
            {
                if (m.MemberType == MemberTypes.Method
                    && !IsObsolete(m)
                    && !DontExport(m)
                    && isUsefullMethod((MethodInfo)m))
                    methods.Add((MethodBase)m);
            }
            methods.Sort((a, b) =>
           {
               return a.GetParameters().Length - b.GetParameters().Length;
           });
            return methods.ToArray();
        }

        private void WriteFunctionImpl(StreamWriter file, MethodInfo m, Type t, BindingFlags bf)
        {
            WriteTry(file);
            MethodBase[] cons = GetMethods(t, m.Name, bf);

            if (cons.Length == 1) // no override function
            {
                if (isUsefullMethod(m) && !m.ReturnType.ContainsGenericParameters && !m.ContainsGenericParameters) // don't support generic method
                    WriteFunctionCall(m, file, t, bf);
                else
                {
                    WriteError(file, "No matched override function to call");
                }
            }
            else // 2 or more override function
            {
                Write(file, "int argc = LuaDLL.lua_gettop(l);");

                bool first = true;
                for (int n = 0; n < cons.Length; n++)
                {
                    if (cons[n].MemberType == MemberTypes.Method)
                    {
                        MethodInfo mi = cons[n] as MethodInfo;

                        ParameterInfo[] pars = mi.GetParameters();
                        if (isUsefullMethod(mi)
                            && !mi.ReturnType.ContainsGenericParameters
                            /*&& !ContainGeneric(pars)*/) // don't support generic method
                        {
                            bool isExtension = IsExtensionMethod(mi) && (bf & BindingFlags.Instance) == BindingFlags.Instance;
                            if (isUniqueArgsCount(cons, mi))
                                Write(file, "{0}(argc=={1}){{", first ? "if" : "else if", mi.IsStatic ? mi.GetParameters().Length : mi.GetParameters().Length + 1);
                            else
                                Write(file, "{0}(matchType(l,argc,{1}{2})){{", first ? "if" : "else if", mi.IsStatic && !isExtension ? 1 : 2, TypeDecl(pars, isExtension ? 1 : 0));
                            WriteFunctionCall(mi, file, t, bf);
                            Write(file, "}");
                            first = false;
                        }
                    }
                }
                WriteError(file, "No matched override function to call");
            }
            WriteCatchExecption(file);
            Write(file, "}");
        }

        private bool isUniqueArgsCount(MethodBase[] cons, MethodBase mi)
        {
            foreach (MethodBase member in cons)
            {
                MethodBase m = (MethodBase)member;
                if (m != mi && mi.GetParameters().Length == m.GetParameters().Length)
                    return false;
            }
            return true;
        }

        private void WriteCheckSelf(StreamWriter file, Type t)
        {
            if (t.IsValueType)
            {
                Write(file, "{0} self;", TypeDecl(t));
                if (IsBaseType(t))
                    Write(file, "checkType(l,1,out self);");
                else
                    Write(file, "checkValueType(l,1,out self);");
            }
            else
                Write(file, "{0} self=({0})checkSelf(l);", TypeDecl(t));
        }

        private void WriteFunctionCall(MethodInfo m, StreamWriter file, Type t, BindingFlags bf)
        {
            bool isExtension = IsExtensionMethod(m) && (bf & BindingFlags.Instance) == BindingFlags.Instance;
            bool hasref = false;
            ParameterInfo[] pars = m.GetParameters();

            int argIndex = 1;
            int parOffset = 0;
            if (!m.IsStatic)
            {
                WriteCheckSelf(file, t);
                argIndex++;
            }
            else if (isExtension)
            {
                WriteCheckSelf(file, t);
                parOffset++;
            }
            for (int n = parOffset; n < pars.Length; n++)
            {
                ParameterInfo p = pars[n];
                string pn = p.ParameterType.Name;
                if (pn.EndsWith("&"))
                {
                    hasref = true;
                }

                bool hasParams = p.IsDefined(typeof(ParamArrayAttribute), false);
                CheckArgument(file, p.ParameterType, n, argIndex, p.IsOut, hasParams);
            }

            string ret = "";
            if (m.ReturnType != typeof(void))
            {
                ret = "var ret=";
            }

            if (m.IsStatic && !isExtension)
            {
                if (m.Name == "op_Multiply")
                    Write(file, "{0}a1*a2;", ret);
                else if (m.Name == "op_Subtraction")
                    Write(file, "{0}a1-a2;", ret);
                else if (m.Name == "op_Addition")
                    Write(file, "{0}a1+a2;", ret);
                else if (m.Name == "op_Division")
                    Write(file, "{0}a1/a2;", ret);
                else if (m.Name == "op_UnaryNegation")
                    Write(file, "{0}-a1;", ret);
                else if (m.Name == "op_Equality")
                    Write(file, "{0}(a1==a2);", ret);
                else if (m.Name == "op_Inequality")
                    Write(file, "{0}(a1!=a2);", ret);
                else if (m.Name == "op_LessThan")
                    Write(file, "{0}(a1<a2);", ret);
                else if (m.Name == "op_GreaterThan")
                    Write(file, "{0}(a2<a1);", ret);
                else if (m.Name == "op_LessThanOrEqual")
                    Write(file, "{0}(a1<=a2);", ret);
                else if (m.Name == "op_GreaterThanOrEqual")
                    Write(file, "{0}(a2<=a1);", ret);
                else
                {
                    Write(file, "{3}{2}.{0}({1});", m.Name, FuncCall(m), TypeDecl(t), ret);
                }
            }
            else
            {
                Write(file, "{2}self.{0}({1});", m.Name, FuncCall(m, parOffset), ret);
            }

            WriteOk(file);
            int retcount = 1;
            if (m.ReturnType != typeof(void))
            {
                WritePushValue(m.ReturnType, file);
                retcount = 2;
            }

            // push out/ref value for return value
            if (hasref)
            {
                for (int n = 0; n < pars.Length; n++)
                {
                    ParameterInfo p = pars[n];

                    if (p.ParameterType.IsByRef)
                    {
                        WritePushValue(p.ParameterType, file, string.Format("a{0}", n + 1));
                        retcount++;
                    }
                }
            }

            if (t.IsValueType && m.ReturnType == typeof(void) && !m.IsStatic)
                Write(file, "setBack(l,self);");

            Write(file, "return {0};", retcount);
        }

        private string SimpleType(Type t)
        {
            string tn = t.Name;
            switch (tn)
            {
                case "Single":
                    return "float";

                case "String":
                    return "string";

                case "Double":
                    return "double";

                case "Boolean":
                    return "bool";

                case "Int32":
                    return "int";

                case "Object":
                    return FullName(t);

                default:
                    tn = TypeDecl(t);
                    tn = tn.Replace("System.Collections.Generic.", "");
                    tn = tn.Replace("System.Object", "object");
                    return tn;
            }
        }

        private void WritePushValue(Type t, StreamWriter file)
        {
            if (t.IsEnum)
                Write(file, "pushEnum(l,(int)ret);");
            else
                Write(file, "pushValue(l,ret);");
        }

        private void WritePushValue(Type t, StreamWriter file, string ret)
        {
            if (t.IsEnum)
                Write(file, "pushEnum(l,(int){0});", ret);
            else
                Write(file, "pushValue(l,{0});", ret);
        }

        private void Write(StreamWriter file, string fmt, params object[] args)
        {
            fmt = System.Text.RegularExpressions.Regex.Replace(fmt, @"\r\n?|\n|\r", NewLine);

            if (fmt.StartsWith("}")) indent--;

            for (int n = 0; n < indent; n++)
                file.Write("\t");

            if (args.Length == 0)
                file.WriteLine(fmt);
            else
            {
                string line = string.Format(fmt, args);
                file.WriteLine(line);
            }

            if (fmt.EndsWith("{")) indent++;
        }

        private void CheckArgument(StreamWriter file, Type t, int n, int argstart, bool isout, bool isparams)
        {
            Write(file, "{0} a{1};", TypeDecl(t), n + 1);

            if (!isout)
            {
                if (t.IsEnum)
                    Write(file, "checkEnum(l,{0},out a{1});", n + argstart, n + 1);
                else if (t.BaseType == typeof(System.MulticastDelegate))
                {
                    tryMake(t);
                    Write(file, "LuaDelegation.checkDelegate(l,{0},out a{1});", n + argstart, n + 1);
                }
                else if (isparams)
                {
                    if (t.GetElementType().IsValueType && !IsBaseType(t.GetElementType()))
                        Write(file, "checkValueParams(l,{0},out a{1});", n + argstart, n + 1);
                    else
                        Write(file, "checkParams(l,{0},out a{1});", n + argstart, n + 1);
                }
                else if (IsValueType(t))
                {
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
                        Write(file, "checkNullable(l,{0},out a{1});", n + argstart, n + 1);
                    else
                        Write(file, "checkValueType(l,{0},out a{1});", n + argstart, n + 1);
                }
                else
                    Write(file, "checkType(l,{0},out a{1});", n + argstart, n + 1);
            }
        }

        private bool IsValueType(Type t)
        {
            return t.BaseType == typeof(ValueType) && !IsBaseType(t);
        }

        private bool IsBaseType(Type t)
        {
            if (t.IsByRef)
            {
                t = t.GetElementType();
            }
            return t.IsPrimitive || LuaObject.isImplByLua(t);
        }

        private string FullName(string str)
        {
            if (str == null)
            {
                throw new NullReferenceException();
            }
            return RemoveRef(str.Replace("+", "."));
        }

        private string TypeDecl(Type t)
        {
            if (t.IsGenericType)
            {
                string ret = GenericBaseName(t);

                string gs = "";
                gs += "<";
                Type[] types = t.GetGenericArguments();
                for (int n = 0; n < types.Length; n++)
                {
                    gs += TypeDecl(types[n]);
                    if (n < types.Length - 1)
                        gs += ",";
                }
                gs += ">";

                ret = Regex.Replace(ret, @"`\d", gs);

                return ret;
            }
            if (t.IsArray)
            {
                return TypeDecl(t.GetElementType()) + "[]";
            }
            else
                return RemoveRef(t.ToString(), false);
        }

        private string ExportName(Type t)
        {
            if (t.IsGenericType)
            {
                return string.Format("Lua_{0}_{1}", _Name(GenericBaseName(t)), _Name(GenericName(t)));
            }
            else
            {
                string name = RemoveRef(t.FullName, true);
                name = "Lua_" + name;
                return name.Replace(".", "_");
            }
        }

        private string FullName(Type t)
        {
            if (t.FullName == null)
            {
                Debug.Log(t.Name);
                return t.Name;
            }
            return FullName(t.FullName);
        }

        private string FuncCall(MethodBase m, int parOffset = 0)
        {
            string str = "";
            ParameterInfo[] pars = m.GetParameters();
            for (int n = parOffset; n < pars.Length; n++)
            {
                ParameterInfo p = pars[n];
                if (p.ParameterType.IsByRef && p.IsOut)
                    str += string.Format("out a{0}", n + 1);
                else if (p.ParameterType.IsByRef)
                    str += string.Format("ref a{0}", n + 1);
                else
                    str += string.Format("a{0}", n + 1);
                if (n < pars.Length - 1)
                    str += ",";
            }
            return str;
        }
    }
}