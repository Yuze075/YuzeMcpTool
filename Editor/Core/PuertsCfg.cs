#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Puerts;
using UnityEngine;

namespace YuzeToolkit
{
    /// <summary>
    /// PuerTS generation configuration for the Unity MCP eval environment.
    /// Use Tools > PuerTS > Generate C# Static Wrappers for bindings, or Generate index.d.ts for typings.
    /// </summary>
    [Configure]
    public sealed class PuertsCfg
    {
        [Binding]
        private static IEnumerable<Type> Bindings
        {
            get
            {
                return new List<Type>
                {
                    typeof(EvalToolRegistry),
                    typeof(EvalValueFormatter),
                    typeof(Resources),
                    typeof(TextAsset),
                    typeof(Debug),
                    typeof(Application),
                    typeof(Time),
                    typeof(Screen),
                    typeof(Mathf),
                    typeof(System.Array),
                    typeof(GameObject),
                    typeof(Component),
                    typeof(Transform),
                    typeof(Camera),
                    typeof(UnityEngine.Object),
                    typeof(Vector2),
                    typeof(Vector3),
                    typeof(Quaternion),
                    typeof(Color),
                    typeof(Action<string>),
                    typeof(Action<Action<string>>),
                };
            }
        }

        [Filter]
        private static BindingMode FilterUnityEditorMembers(MemberInfo memberInfo)
        {
            if (IsUnityEditorType(memberInfo.DeclaringType)) return BindingMode.DontBinding;

            if (memberInfo is MethodInfo method)
            {
                if (IsUnityEditorType(method.ReturnType)) return BindingMode.DontBinding;
                if (method.GetParameters().Any(parameter => IsUnityEditorType(parameter.ParameterType)))
                    return BindingMode.DontBinding;
            }

            if (memberInfo is ConstructorInfo constructor &&
                constructor.GetParameters().Any(parameter => IsUnityEditorType(parameter.ParameterType)))
                return BindingMode.DontBinding;

            if (memberInfo is PropertyInfo property && IsUnityEditorType(property.PropertyType))
                return BindingMode.DontBinding;

            if (memberInfo is FieldInfo field && IsUnityEditorType(field.FieldType))
                return BindingMode.DontBinding;

            return BindingMode.FastBinding;
        }

        [Filter]
        private static bool FilterUnityEditorTypes(FilterAction filterAction, Type type)
        {
            return filterAction == FilterAction.DisallowedType && IsUnityEditorType(type);
        }

        private static bool IsUnityEditorType(Type? type)
        {
            if (type == null) return false;

            while (type.IsArray || type.IsByRef || type.IsPointer)
                type = type.GetElementType();

            if (type == null) return false;

            if (type.IsGenericType && type.GetGenericArguments().Any(IsUnityEditorType))
                return true;

            var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
            var fullName = type.FullName ?? type.Name;
            return assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                   fullName.StartsWith("UnityEditor.", StringComparison.Ordinal);
        }
    }
}
