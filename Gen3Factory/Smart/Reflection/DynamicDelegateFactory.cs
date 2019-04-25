namespace Smart.Reflection
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;

    using Smart.ComponentModel;
    using Smart.Reflection.Emit;

    public sealed partial class DynamicDelegateFactory : IDelegateFactory
    {
        // Cache

        private readonly ConcurrentDictionary<Type, Func<int, Array>> arrayAllocatorCache = new ConcurrentDictionary<Type, Func<int, Array>>();

        private readonly ConcurrentDictionary<ConstructorInfo, Func<object[], object>> factoryCache = new ConcurrentDictionary<ConstructorInfo, Func<object[], object>>();

        private readonly ConcurrentDictionary<ConstructorInfo, Delegate> factoryDelegateCache = new ConcurrentDictionary<ConstructorInfo, Delegate>();

        private readonly ConcurrentDictionary<ConstructorInfo, Delegate> typedFactoryCache = new ConcurrentDictionary<ConstructorInfo, Delegate>();

        private readonly ConcurrentDictionary<PropertyInfo, Func<object, object>> getterCache = new ConcurrentDictionary<PropertyInfo, Func<object, object>>();

        private readonly ConcurrentDictionary<PropertyInfo, Func<object, object>> extensionGetterCache = new ConcurrentDictionary<PropertyInfo, Func<object, object>>();

        private readonly ConcurrentDictionary<PropertyInfo, Action<object, object>> setterCache = new ConcurrentDictionary<PropertyInfo, Action<object, object>>();

        private readonly ConcurrentDictionary<PropertyInfo, Action<object, object>> extensionSetterCache = new ConcurrentDictionary<PropertyInfo, Action<object, object>>();

        private readonly ConcurrentDictionary<PropertyInfo, Delegate> typedGetterCache = new ConcurrentDictionary<PropertyInfo, Delegate>();

        private readonly ConcurrentDictionary<PropertyInfo, Delegate> typedExtensionGetterCache = new ConcurrentDictionary<PropertyInfo, Delegate>();

        private readonly ConcurrentDictionary<PropertyInfo, Delegate> typedSetterCache = new ConcurrentDictionary<PropertyInfo, Delegate>();

        private readonly ConcurrentDictionary<PropertyInfo, Delegate> typedExtensionSetterCache = new ConcurrentDictionary<PropertyInfo, Delegate>();

        // Property

        public static DynamicDelegateFactory Default { get; } = new DynamicDelegateFactory();

        // Constructor

        private DynamicDelegateFactory()
        {
        }

        //--------------------------------------------------------------------------------
        // Array
        //--------------------------------------------------------------------------------

        public Func<int, Array> CreateArrayAllocator(Type type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return arrayAllocatorCache.GetOrAdd(type, CreateArrayAllocatorInternal);
        }

        private Func<int, Array> CreateArrayAllocatorInternal(Type type)
        {
            var dynamic = new DynamicMethod(string.Empty, typeof(Array), new[] { typeof(object), typeof(int) }, true);
            var il = dynamic.GetILGenerator();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Newarr, type);
            il.Emit(OpCodes.Ret);

            return (Func<int, Array>)dynamic.CreateDelegate(typeof(Func<int, Array>), null);
        }

        //--------------------------------------------------------------------------------
        // Factory
        //--------------------------------------------------------------------------------

        public Func<object[], object> CreateFactory(ConstructorInfo ci)
        {
            if (ci is null)
            {
                throw new ArgumentNullException(nameof(ci));
            }

            return factoryCache.GetOrAdd(ci, CreateFactoryInternal);
        }

        // Factory

        public Delegate CreateFactoryDelegate(ConstructorInfo ci)
        {
            return typedFactoryCache
                .GetOrAdd(ci, x => CreateFactoryInternal(
                    ci,
                    ci.DeclaringType,
                    ci.GetParameters().Select(p => p.ParameterType).ToArray()));
        }

        // Factory Helper

        private Func<object[], object> CreateFactoryInternal(ConstructorInfo ci)
        {
            var returnType = ci.DeclaringType.IsValueType ? typeof(object) : ci.DeclaringType;

            var dynamic = new DynamicMethod(string.Empty, returnType, new[] { typeof(object), typeof(object[]) }, true);
            var il = dynamic.GetILGenerator();

            for (var i = 0; i < ci.GetParameters().Length; i++)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.EmitLdcI4(i);
                il.Emit(OpCodes.Ldelem_Ref);
                il.EmitTypeConversion(ci.GetParameters()[i].ParameterType);
            }

            il.Emit(OpCodes.Newobj, ci);
            if (ci.DeclaringType.IsValueType)
            {
                il.Emit(OpCodes.Box, ci.DeclaringType);
            }
            il.Emit(OpCodes.Ret);

            var delegateType = typeof(Func<,>).MakeGenericType(typeof(object[]), returnType);
            return (Func<object[], object>)dynamic.CreateDelegate(delegateType, null);
        }

        private static readonly Dictionary<int, Type> FactoryDelegateTypes = new Dictionary<int, Type>
        {
            { 0, typeof(Func<>) },
            { 1, typeof(Func<,>) },
            { 2, typeof(Func<,,>) },
            { 3, typeof(Func<,,,>) },
            { 4, typeof(Func<,,,,>) },
            { 5, typeof(Func<,,,,,>) },
            { 6, typeof(Func<,,,,,,>) },
            { 7, typeof(Func<,,,,,,,>) },
            { 8, typeof(Func<,,,,,,,,>) },
            { 9, typeof(Func<,,,,,,,,,>) },
            { 10, typeof(Func<,,,,,,,,,,>) },
            { 11, typeof(Func<,,,,,,,,,,,>) },
            { 12, typeof(Func<,,,,,,,,,,,,>) },
            { 13, typeof(Func<,,,,,,,,,,,,,>) },
            { 14, typeof(Func<,,,,,,,,,,,,,,>) },
            { 15, typeof(Func<,,,,,,,,,,,,,,,>) },
            { 16, typeof(Func<,,,,,,,,,,,,,,,,>) }
        };

        private static Delegate CreateFactoryInternal(ConstructorInfo ci, Type returnType, Type[] argumentTypes)
        {
            if (!FactoryDelegateTypes.TryGetValue(argumentTypes.Length, out var delegateOpenType))
            {
                throw new ArgumentNullException(nameof(argumentTypes));
            }

            var parameterTypes = new Type[argumentTypes.Length + 1];
            parameterTypes[0] = typeof(object);
            Array.Copy(argumentTypes, 0, parameterTypes, 1, argumentTypes.Length);

            var typeArguments = new Type[argumentTypes.Length + 1];
            Array.Copy(argumentTypes, 0, typeArguments, 0, argumentTypes.Length);
            typeArguments[typeArguments.Length - 1] = returnType;

            var delegateType = delegateOpenType.MakeGenericType(typeArguments);

            var dynamic = new DynamicMethod(string.Empty, returnType, parameterTypes, true);
            var il = dynamic.GetILGenerator();

            for (var i = 0; i < ci.GetParameters().Length; i++)
            {
                il.EmitLdarg(i + 1);
                if (argumentTypes[i] == typeof(object))
                {
                    il.EmitTypeConversion(ci.GetParameters()[i].ParameterType);
                }
            }

            il.Emit(OpCodes.Newobj, ci);
            if ((returnType == typeof(object)) && (ci.DeclaringType.IsValueType))
            {
                il.Emit(OpCodes.Box, ci.DeclaringType);
            }
            il.Emit(OpCodes.Ret);

            return dynamic.CreateDelegate(delegateType, null);
        }

        //--------------------------------------------------------------------------------
        // Accessor
        //--------------------------------------------------------------------------------

        public Func<object, object> CreateGetter(PropertyInfo pi)
        {
            return CreateGetter(pi, true);
        }

        public Func<object, object> CreateGetter(PropertyInfo pi, bool extension)
        {
            if (pi is null)
            {
                throw new ArgumentNullException(nameof(pi));
            }

            var holderType = !extension ? null : ValueHolderHelper.FindValueHolderType(pi);
            var isValueHolder = holderType != null;
            var tpi = isValueHolder ? ValueHolderHelper.GetValueTypeProperty(holderType) : pi;

            return extension
                ? extensionGetterCache.GetOrAdd(pi, x => CreateGetterInternal(x, tpi, isValueHolder))
                : getterCache.GetOrAdd(pi, x => CreateGetterInternal(x, tpi, false));
        }

        public Action<object, object> CreateSetter(PropertyInfo pi)
        {
            return CreateSetter(pi, true);
        }

        public Action<object, object> CreateSetter(PropertyInfo pi, bool extension)
        {
            if (pi is null)
            {
                throw new ArgumentNullException(nameof(pi));
            }

            var holderType = !extension ? null : ValueHolderHelper.FindValueHolderType(pi);
            var isValueHolder = holderType != null;
            var tpi = isValueHolder ? ValueHolderHelper.GetValueTypeProperty(holderType) : pi;

            return extension
                ? extensionSetterCache.GetOrAdd(pi, x => CreateSetterInternal(x, tpi, isValueHolder))
                : setterCache.GetOrAdd(pi, x => CreateSetterInternal(x, tpi, false));
        }

        // Accessor

        public Func<T, TMember> CreateGetter<T, TMember>(PropertyInfo pi)
        {
            return CreateGetter<T, TMember>(pi, true);
        }

        public Func<T, TMember> CreateGetter<T, TMember>(PropertyInfo pi, bool extension)
        {
            if (pi is null)
            {
                throw new ArgumentNullException(nameof(pi));
            }

            if (pi.DeclaringType != typeof(T))
            {
                throw new ArgumentException($"Invalid type parameter. name=[{pi.Name}]", nameof(pi));
            }

            var holderType = !extension ? null : ValueHolderHelper.FindValueHolderType(pi);
            var isValueHolder = holderType != null;
            var tpi = isValueHolder ? ValueHolderHelper.GetValueTypeProperty(holderType) : pi;

            if (tpi.PropertyType != typeof(TMember))
            {
                throw new ArgumentException($"Invalid type parameter. name=[{pi.Name}]", nameof(pi));
            }

            return (Func<T, TMember>)(extension
                ? typedExtensionGetterCache.GetOrAdd(pi, x => CreateGetterInternal(x, tpi, isValueHolder, typeof(T), typeof(TMember)))
                : typedGetterCache.GetOrAdd(pi, x => CreateGetterInternal(x, tpi, false, typeof(T), typeof(TMember))));
        }

        public Action<T, TMember> CreateSetter<T, TMember>(PropertyInfo pi)
        {
            return CreateSetter<T, TMember>(pi, true);
        }

        public Action<T, TMember> CreateSetter<T, TMember>(PropertyInfo pi, bool extension)
        {
            if (pi is null)
            {
                throw new ArgumentNullException(nameof(pi));
            }

            if (pi.DeclaringType != typeof(T))
            {
                throw new ArgumentException($"Invalid type parameter. name=[{pi.Name}]", nameof(pi));
            }

            var holderType = !extension ? null : ValueHolderHelper.FindValueHolderType(pi);
            var isValueHolder = holderType != null;
            var tpi = isValueHolder ? ValueHolderHelper.GetValueTypeProperty(holderType) : pi;

            if (tpi.PropertyType != typeof(TMember))
            {
                throw new ArgumentException($"Invalid type parameter. name=[{pi.Name}]", nameof(pi));
            }

            return (Action<T, TMember>)(extension
                ? typedExtensionSetterCache.GetOrAdd(pi, x => CreateSetterInternal(x, tpi, isValueHolder, typeof(T), typeof(TMember)))
                : typedSetterCache.GetOrAdd(pi, x => CreateSetterInternal(x, tpi, false, typeof(T), typeof(TMember))));
        }

        // Accessor

        public Delegate CreateGetterDelegate(PropertyInfo pi)
        {
            return CreateGetterDelegate(pi, true);
        }

        public Delegate CreateGetterDelegate(PropertyInfo pi, bool extension)
        {
            if (pi is null)
            {
                throw new ArgumentNullException(nameof(pi));
            }

            var holderType = !extension ? null : ValueHolderHelper.FindValueHolderType(pi);
            var isValueHolder = holderType != null;
            var tpi = isValueHolder ? ValueHolderHelper.GetValueTypeProperty(holderType) : pi;

            return extension
                ? typedExtensionGetterCache.GetOrAdd(pi, x => CreateGetterInternal(x, tpi, isValueHolder, pi.DeclaringType, tpi.PropertyType))
                : typedGetterCache.GetOrAdd(pi, x => CreateGetterInternal(x, tpi, false, pi.DeclaringType, tpi.PropertyType));
        }

        public Delegate CreateSetterDelegate(PropertyInfo pi)
        {
            return CreateSetterDelegate(pi, true);
        }

        public Delegate CreateSetterDelegate(PropertyInfo pi, bool extension)
        {
            if (pi is null)
            {
                throw new ArgumentNullException(nameof(pi));
            }

            var holderType = !extension ? null : ValueHolderHelper.FindValueHolderType(pi);
            var isValueHolder = holderType != null;
            var tpi = isValueHolder ? ValueHolderHelper.GetValueTypeProperty(holderType) : pi;

            return extension
                ? typedExtensionSetterCache.GetOrAdd(pi, x => CreateSetterInternal(x, tpi, isValueHolder, pi.DeclaringType, tpi.PropertyType))
                : typedSetterCache.GetOrAdd(pi, x => CreateSetterInternal(x, tpi, false, pi.DeclaringType, tpi.PropertyType));
        }

        // Accessor helper

        private Func<object, object> CreateGetterInternal(PropertyInfo pi, PropertyInfo tpi, bool isValueHolder)
        {
            // TODO 統合？、object自体の時の不要キャスト？
            var returnType = tpi.PropertyType.IsValueType ? typeof(object) : tpi.PropertyType;

            if (isValueHolder && !pi.CanRead)
            {
                throw new ArgumentException($"Value holder is not readable. name=[{pi.Name}]", nameof(pi));
            }

            if (!tpi.CanRead)
            {
                return null;
            }

            var dynamic = new DynamicMethod(string.Empty, returnType, new[] { typeof(object), typeof(object) }, true);
            var il = dynamic.GetILGenerator();

            if (!pi.GetGetMethod().IsStatic)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Castclass, pi.DeclaringType);
            }

            il.Emit(pi.GetGetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, pi.GetGetMethod());

            if (isValueHolder)
            {
                il.Emit(OpCodes.Callvirt, tpi.GetGetMethod());
            }
            if (tpi.PropertyType.IsValueType)
            {
                il.Emit(OpCodes.Box, tpi.PropertyType);
            }

            il.Emit(OpCodes.Ret);

            var delegateType = typeof(Func<,>).MakeGenericType(typeof(object), returnType);
            return (Func<object, object>)dynamic.CreateDelegate(delegateType, null);
        }

        private Delegate CreateGetterInternal(PropertyInfo pi, PropertyInfo tpi, bool isValueHolder, Type targetType, Type memberType)
        {
            // TODO 統合？
            if (isValueHolder && !pi.CanRead)
            {
                throw new ArgumentException($"Value holder is not readable. name=[{pi.Name}]", nameof(pi));
            }

            if (!tpi.CanRead)
            {
                return null;
            }

            var dynamic = new DynamicMethod(string.Empty, memberType, new[] { typeof(object), targetType }, true);
            var il = dynamic.GetILGenerator();

            if (!pi.GetGetMethod().IsStatic)
            {
                il.Emit(OpCodes.Ldarg_1);
            }

            il.Emit(pi.GetGetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, pi.GetGetMethod());

            if (isValueHolder)
            {
                il.Emit(OpCodes.Callvirt, tpi.GetGetMethod());
            }

            il.Emit(OpCodes.Ret);

            var delegateType = typeof(Func<,>).MakeGenericType(targetType, memberType);
            return dynamic.CreateDelegate(delegateType, null);
        }

        private static readonly Dictionary<Type, Action<ILGenerator>> LdcDictionary = new Dictionary<Type, Action<ILGenerator>>
        {
            { typeof(bool), il => il.Emit(OpCodes.Ldc_I4_0) },
            { typeof(byte), il => il.Emit(OpCodes.Ldc_I4_0) },
            { typeof(char), il => il.Emit(OpCodes.Ldc_I4_0) },
            { typeof(short), il => il.Emit(OpCodes.Ldc_I4_0) },
            { typeof(int), il => il.Emit(OpCodes.Ldc_I4_0) },
            { typeof(sbyte), il => il.Emit(OpCodes.Ldc_I4_0) },
            { typeof(ushort), il => il.Emit(OpCodes.Ldc_I4_0) },
            { typeof(uint), il => il.Emit(OpCodes.Ldc_I4_0) },      // Simplicity
            { typeof(long), il => il.Emit(OpCodes.Ldc_I8, 0L) },
            { typeof(ulong), il => il.Emit(OpCodes.Ldc_I8, 0L) },   // Simplicity
            { typeof(float), il => il.Emit(OpCodes.Ldc_R4, 0f) },
            { typeof(double), il => il.Emit(OpCodes.Ldc_R8, 0d) },
            { typeof(IntPtr), il => il.Emit(OpCodes.Ldc_I4_0) },    // Simplicity
            { typeof(UIntPtr), il => il.Emit(OpCodes.Ldc_I4_0) },   // Simplicity
        };

        private Action<object, object> CreateSetterInternal(PropertyInfo pi, PropertyInfo tpi, bool isValueHolder)
        {
            // TODO 統合？、object自体の時の不要キャスト
            if (isValueHolder && !pi.CanRead)
            {
                throw new ArgumentException($"Value holder is not readable. name=[{pi.Name}]", nameof(pi));
            }

            if (!tpi.CanWrite)
            {
                return null;
            }

            var isStatic = isValueHolder ? pi.GetGetMethod().IsStatic : pi.GetSetMethod().IsStatic;

            var dynamic = new DynamicMethod(string.Empty, typeof(void), new[] { typeof(object), typeof(object), typeof(object) }, true);
            var il = dynamic.GetILGenerator();

            if (tpi.PropertyType.IsValueType)
            {
                var hasValue = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Brtrue_S, hasValue);

                // null
                if (!isStatic)
                {
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Castclass, pi.DeclaringType);
                }

                if (isValueHolder)
                {
                    il.Emit(pi.GetGetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, pi.GetGetMethod());
                }

                var type = tpi.PropertyType.IsEnum ? tpi.PropertyType.GetEnumUnderlyingType() : tpi.PropertyType;
                if (LdcDictionary.TryGetValue(type, out var action))
                {
                    action(il);
                }
                else
                {
                    var local = il.DeclareLocal(tpi.PropertyType);
                    il.Emit(OpCodes.Ldloca_S, local);
                    il.Emit(OpCodes.Initobj, tpi.PropertyType);
                    il.Emit(OpCodes.Ldloc_0);
                }

                il.Emit(tpi.GetSetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, tpi.GetSetMethod());

                il.Emit(OpCodes.Ret);

                // not null
                il.MarkLabel(hasValue);

                if (!isStatic)
                {
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Castclass, pi.DeclaringType);
                }

                if (isValueHolder)
                {
                    il.Emit(pi.GetGetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, pi.GetGetMethod());
                }

                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Unbox_Any, tpi.PropertyType);

                il.Emit(tpi.GetSetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, tpi.GetSetMethod());

                il.Emit(OpCodes.Ret);
            }
            else
            {
                if (!isStatic)
                {
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Castclass, pi.DeclaringType);
                }

                if (isValueHolder)
                {
                    il.Emit(pi.GetGetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, pi.GetGetMethod());
                }

                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Castclass, tpi.PropertyType);

                il.Emit(tpi.GetSetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, tpi.GetSetMethod());

                il.Emit(OpCodes.Ret);
            }

            return (Action<object, object>)dynamic.CreateDelegate(typeof(Action<object, object>), null);
        }

        private Delegate CreateSetterInternal(PropertyInfo pi, PropertyInfo tpi, bool isValueHolder, Type targetType, Type memberType)
        {
            // TODO 統合？
            if (pi.DeclaringType != targetType)
            {
                throw new ArgumentException($"Invalid type parameter. name=[{pi.Name}]", nameof(pi));
            }

            if (tpi.PropertyType != memberType)
            {
                throw new ArgumentException($"Invalid type parameter. name=[{pi.Name}]", nameof(pi));
            }

            if (isValueHolder && !pi.CanRead)
            {
                throw new ArgumentException($"Value holder is not readable. name=[{pi.Name}]", nameof(pi));
            }

            if (!tpi.CanWrite)
            {
                return null;
            }

            var isStatic = isValueHolder ? pi.GetGetMethod().IsStatic : pi.GetSetMethod().IsStatic;

            var dynamic = new DynamicMethod(string.Empty, typeof(void), new[] { typeof(object), targetType, memberType }, true);
            var il = dynamic.GetILGenerator();

            if (!isStatic)
            {
                il.Emit(OpCodes.Ldarg_1);
            }

            if (isValueHolder)
            {
                il.Emit(pi.GetGetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, pi.GetGetMethod());
            }

            il.Emit(OpCodes.Ldarg_2);

            il.Emit(tpi.GetSetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, tpi.GetSetMethod());

            il.Emit(OpCodes.Ret);

            var delegateType = typeof(Action<,>).MakeGenericType(targetType, memberType);
            return dynamic.CreateDelegate(delegateType, null);
        }

        //--------------------------------------------------------------------------------
        // Etc
        //--------------------------------------------------------------------------------

        public Type GetExtendedPropertyType(PropertyInfo pi)
        {
            var holderType = ValueHolderHelper.FindValueHolderType(pi);
            var tpi = holderType is null ? pi : ValueHolderHelper.GetValueTypeProperty(holderType);
            return tpi.PropertyType;
        }
    }
}
