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
        private static readonly Type VoidType = typeof(void);

        private static readonly Type ObjectType = typeof(object);

        private static readonly Type ObjectArrayType = typeof(object[]);

        private static readonly Type Int32Type = typeof(int);

        private static readonly Type ArrayType = typeof(Array);

        // ArrayAllocator

        private static readonly Type[] ArrayAllocatorParameterTypes = { ObjectType, Int32Type };

        private static readonly Type ArrayAllocatorType = typeof(Func<int, Array>);

        // Factory

        private static readonly Type[] FactoryParameterTypes = { ObjectType, ObjectArrayType };

        // Accessor

        private static readonly Type[] GetterParameterTypes = { ObjectType, ObjectType };

        private static readonly Type[] SetterParameterTypes = { ObjectType, ObjectType, ObjectType };

        private static readonly Type SetterType = typeof(Action<object, object>);

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
            var dynamic = new DynamicMethod(string.Empty, ArrayType, ArrayAllocatorParameterTypes, true);
            var il = dynamic.GetILGenerator();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Newarr, type);
            il.Emit(OpCodes.Ret);

            return (Func<int, Array>)dynamic.CreateDelegate(ArrayAllocatorType, null);
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
                .GetOrAdd(ci, x =>
                {
                    var types = new Type[ci.GetParameters().Length + 1];
                    for (var i = 0; i < types.Length; i++)
                    {
                        types[i] = ci.GetParameters()[i].ParameterType;
                    }
                    types[types.Length - 1] = ci.DeclaringType;

                    var delegateType = typeof(Func<>).MakeGenericType(types);
                    return CreateFactoryInternal(
                        ci,
                        ci.DeclaringType,
                        ci.GetParameters().Select(p => p.ParameterType).ToArray(),
                        delegateType);
                });
        }

        // Factory Helper

        private Func<object[], object> CreateFactoryInternal(ConstructorInfo ci)
        {
            var dynamic = new DynamicMethod(string.Empty, ci.DeclaringType, FactoryParameterTypes, true);
            var il = dynamic.GetILGenerator();

            for (var i = 0; i < ci.GetParameters().Length; i++)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.EmitLdcI4(i);
                il.Emit(OpCodes.Ldelem_Ref);
                var paramType = ci.GetParameters()[i].ParameterType;
                il.Emit(paramType.GetTypeInfo().IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, paramType);
            }

            il.Emit(OpCodes.Newobj, ci);
            il.Emit(OpCodes.Ret);

            var delegateType = typeof(Func<,>).MakeGenericType(ObjectArrayType, ci.DeclaringType);
            return (Func<object[], object>)dynamic.CreateDelegate(delegateType, null);
        }

        private static Delegate CreateFactoryInternal(ConstructorInfo ci, Type returnType, Type[] parameterTypes, Type delegateType)
        {
            // TODO 生粋のパラメータタイプから、作る方式に変更？
            // TODO Funcはメソッド定義、 Dynamicはobject付き？

            var dynamic = new DynamicMethod(string.Empty, returnType, parameterTypes, true);
            var il = dynamic.GetILGenerator();

            for (var i = 0; i < ci.GetParameters().Length; i++)
            {
                il.EmitLdarg(i + 1);
                if (parameterTypes[i + 1] != ci.GetParameters()[i].ParameterType)
                {
                    il.EmitTypeConversion(ci.GetParameters()[i].ParameterType);
                }
            }

            il.Emit(OpCodes.Newobj, ci);
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

            return extension
                ? extensionGetterCache.GetOrAdd(pi, x => CreateGetterInternal(x, true))
                : getterCache.GetOrAdd(pi, x => CreateGetterInternal(x, false));
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

            return extension
                ? extensionSetterCache.GetOrAdd(pi, x => CreateSetterInternal(x, true))
                : setterCache.GetOrAdd(pi, x => CreateSetterInternal(x, false));
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

            return (Func<T, TMember>)(extension
                ? typedExtensionGetterCache.GetOrAdd(pi, x => CreateGetterInternal<T, TMember>(x, true))
                : typedGetterCache.GetOrAdd(pi, x => CreateGetterInternal<T, TMember>(x, false)));
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

            return (Action<T, TMember>)(extension
                ? typedExtensionSetterCache.GetOrAdd(pi, x => CreateSetterInternal<T, TMember>(x, true))
                : typedSetterCache.GetOrAdd(pi, x => CreateSetterInternal<T, TMember>(x, false)));
        }

        // Accessor

        public Delegate CreateGetterDelegate(PropertyInfo pi)
        {
            throw new NotImplementedException();
        }

        public Delegate CreateGetterDelegate(PropertyInfo pi, bool extension)
        {
            throw new NotImplementedException();
        }

        public Delegate CreateSetterDelegate(PropertyInfo pi)
        {
            throw new NotImplementedException();
        }

        public Delegate CreateSetterDelegate(PropertyInfo pi, bool extension)
        {
            throw new NotImplementedException();
        }

        // Accessor helper

        private Func<object, object> CreateGetterInternal(PropertyInfo pi, bool extension)
        {
            var holderType = !extension ? null : ValueHolderHelper.FindValueHolderType(pi);
            var isValueProperty = holderType != null;
            var tpi = isValueProperty ? ValueHolderHelper.GetValueTypeProperty(holderType) : pi;
            var returnType = tpi.PropertyType.IsValueType ? ObjectType : tpi.PropertyType;

            if (isValueProperty && !pi.CanRead)
            {
                throw new ArgumentException($"Value holder is not readable. name=[{pi.Name}]", nameof(pi));
            }

            if (!tpi.CanRead)
            {
                return null;
            }

            var dynamic = new DynamicMethod(string.Empty, returnType, GetterParameterTypes, true);
            var il = dynamic.GetILGenerator();

            if (!pi.GetGetMethod().IsStatic)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Castclass, pi.DeclaringType);
            }

            il.Emit(pi.GetGetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, pi.GetGetMethod());

            if (isValueProperty)
            {
                il.Emit(OpCodes.Callvirt, tpi.GetGetMethod());
            }
            if (tpi.PropertyType.IsValueType)
            {
                il.Emit(OpCodes.Box, tpi.PropertyType);
            }

            il.Emit(OpCodes.Ret);

            var delegateType = typeof(Func<,>).MakeGenericType(ObjectType, returnType);
            return (Func<object, object>)dynamic.CreateDelegate(delegateType, null);
        }

        private Delegate CreateGetterInternal<T, TMember>(PropertyInfo pi, bool extension)
        {
            var holderType = !extension ? null : ValueHolderHelper.FindValueHolderType(pi);
            var isValueProperty = holderType != null;
            var tpi = isValueProperty ? ValueHolderHelper.GetValueTypeProperty(holderType) : pi;

            if (pi.DeclaringType != typeof(T))
            {
                throw new ArgumentException($"Invalid type parameter. name=[{pi.Name}]", nameof(pi));
            }

            if (tpi.PropertyType != typeof(TMember))
            {
                throw new ArgumentException($"Invalid type parameter. name=[{pi.Name}]", nameof(pi));
            }

            if (isValueProperty && !pi.CanRead)
            {
                throw new ArgumentException($"Value holder is not readable. name=[{pi.Name}]", nameof(pi));
            }

            if (!tpi.CanRead)
            {
                return null;
            }

            var dynamic = new DynamicMethod(string.Empty, typeof(TMember), new[] { ObjectType, typeof(T) }, true);
            var il = dynamic.GetILGenerator();

            if (!pi.GetGetMethod().IsStatic)
            {
                il.Emit(OpCodes.Ldarg_1);
            }

            il.Emit(pi.GetGetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, pi.GetGetMethod());

            if (isValueProperty)
            {
                il.Emit(OpCodes.Callvirt, tpi.GetGetMethod());
            }

            il.Emit(OpCodes.Ret);

            return dynamic.CreateDelegate(typeof(Func<T, TMember>), null);
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

        private Action<object, object> CreateSetterInternal(PropertyInfo pi, bool extension)
        {
            var holderType = !extension ? null : ValueHolderHelper.FindValueHolderType(pi);
            var isValueProperty = holderType != null;
            var tpi = isValueProperty ? ValueHolderHelper.GetValueTypeProperty(holderType) : pi;

            if (isValueProperty && !pi.CanRead)
            {
                throw new ArgumentException($"Value holder is not readable. name=[{pi.Name}]", nameof(pi));
            }

            if (!tpi.CanWrite)
            {
                return null;
            }

            var isStatic = isValueProperty ? pi.GetGetMethod().IsStatic : pi.GetSetMethod().IsStatic;

            var dynamic = new DynamicMethod(string.Empty, VoidType, SetterParameterTypes, true);
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

                if (isValueProperty)
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

                if (isValueProperty)
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

                if (isValueProperty)
                {
                    il.Emit(pi.GetGetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, pi.GetGetMethod());
                }

                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Castclass, tpi.PropertyType);

                il.Emit(tpi.GetSetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, tpi.GetSetMethod());

                il.Emit(OpCodes.Ret);
            }

            return (Action<object, object>)dynamic.CreateDelegate(SetterType, null);
        }

        private Delegate CreateSetterInternal<T, TMember>(PropertyInfo pi, bool extension)
        {
            var holderType = !extension ? null : ValueHolderHelper.FindValueHolderType(pi);
            var isValueProperty = holderType != null;
            var tpi = isValueProperty ? ValueHolderHelper.GetValueTypeProperty(holderType) : pi;

            if (pi.DeclaringType != typeof(T))
            {
                throw new ArgumentException($"Invalid type parameter. name=[{pi.Name}]", nameof(pi));
            }

            if (tpi.PropertyType != typeof(TMember))
            {
                throw new ArgumentException($"Invalid type parameter. name=[{pi.Name}]", nameof(pi));
            }

            if (isValueProperty && !pi.CanRead)
            {
                throw new ArgumentException($"Value holder is not readable. name=[{pi.Name}]", nameof(pi));
            }

            if (!tpi.CanWrite)
            {
                return null;
            }

            var isStatic = isValueProperty ? pi.GetGetMethod().IsStatic : pi.GetSetMethod().IsStatic;

            var dynamic = new DynamicMethod(string.Empty, VoidType, new[] { ObjectType, typeof(T), typeof(TMember) }, true);
            var il = dynamic.GetILGenerator();

            if (!isStatic)
            {
                il.Emit(OpCodes.Ldarg_1);
            }

            if (isValueProperty)
            {
                il.Emit(pi.GetGetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, pi.GetGetMethod());
            }

            il.Emit(OpCodes.Ldarg_2);

            il.Emit(tpi.GetSetMethod().IsStatic ? OpCodes.Call : OpCodes.Callvirt, tpi.GetSetMethod());

            il.Emit(OpCodes.Ret);

            return dynamic.CreateDelegate(typeof(Action<T, TMember>), null);
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
