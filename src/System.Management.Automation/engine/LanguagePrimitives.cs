// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Management.Automation.Internal;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

using MethodCacheEntry = System.Management.Automation.DotNetAdapter.MethodCacheEntry;
#if !UNIX
using System.DirectoryServices;
using System.Management;
#endif

#pragma warning disable 1634, 1691 // Stops compiler from warning about unknown warnings
#pragma warning disable 56500

namespace System.Management.Automation
{
    #region public type converters
    /// <summary>
    /// Defines a base class implemented when you need to customize the type conversion for a target class.
    /// </summary>
    /// <remarks>
    /// There are two ways of associating the PSTypeConverter with its target class:
    ///     - Through the type configuration file.
    ///     - By applying a TypeConverterAttribute to the target class.
    ///
    /// Unlike System.ComponentModel.TypeConverter, PSTypeConverter can be applied to a family of types (like all types derived from System.Enum).
    /// PSTypeConverter has two main differences from TypeConverter:
    ///     - It can be applied to a family of types and not only the one type as in TypeConverter. In order to do that
    /// ConvertFrom and CanConvertFrom receive destinationType to know to which type specifically we are converting sourceValue.
    ///     - ConvertTo and ConvertFrom receive formatProvider and ignoreCase.
    /// Other differences to System.ComponentModel.TypeConverter:
    ///     - There is no ITypeDescriptorContext.
    ///     - This class is abstract
    /// </remarks>
    public abstract class PSTypeConverter
    {
        private static object GetSourceValueAsObject(PSObject sourceValue)
        {
            if (sourceValue == null)
            {
                return null;
            }

            if (sourceValue.BaseObject is PSCustomObject)
            {
                return sourceValue;
            }
            else
            {
                return PSObject.Base(sourceValue);
            }
        }

        /// <summary>
        /// Determines if the converter can convert the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter.
        /// </summary>
        /// <param name="sourceValue">Value supposedly *not* of the types supported by this converted to be converted to the <paramref name="destinationType"/> parameter.</param>
        /// <param name="destinationType">One of the types supported by this converter to which the <paramref name="sourceValue"/> parameter should be converted.</param>
        /// <returns>True if the converter can convert the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter, otherwise false.</returns>
        public abstract bool CanConvertFrom(object sourceValue, Type destinationType);

        /// <summary>
        /// Determines if the converter can convert the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter.
        /// </summary>
        /// <param name="sourceValue">Value supposedly *not* of the types supported by this converted to be converted to the <paramref name="destinationType"/> parameter.</param>
        /// <param name="destinationType">One of the types supported by this converter to which the <paramref name="sourceValue"/> parameter should be converted.</param>
        /// <returns>True if the converter can convert the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter, otherwise false.</returns>
        public virtual bool CanConvertFrom(PSObject sourceValue, Type destinationType)
            => CanConvertFrom(GetSourceValueAsObject(sourceValue), destinationType);

        /// <summary>
        /// Converts the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter using formatProvider and ignoreCase.
        /// </summary>
        /// <param name="sourceValue">Value supposedly *not* of the types supported by this converted to be converted to the <paramref name="destinationType"/> parameter.</param>
        /// <param name="destinationType">One of the types supported by this converter to which the <paramref name="sourceValue"/> parameter should be converted to.</param>
        /// <param name="formatProvider">The format provider to use like in IFormattable's ToString.</param>
        /// <param name="ignoreCase">True if case should be ignored.</param>
        /// <returns>The <paramref name="sourceValue"/> parameter converted to the <paramref name="destinationType"/> parameter using formatProvider and ignoreCase.</returns>
        /// <exception cref="InvalidCastException">If no conversion was possible.</exception>
        public abstract object ConvertFrom(object sourceValue, Type destinationType, IFormatProvider formatProvider, bool ignoreCase);

        /// <summary>
        /// Converts the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter using formatProvider and ignoreCase.
        /// </summary>
        /// <param name="sourceValue">Value supposedly *not* of the types supported by this converted to be converted to the <paramref name="destinationType"/> parameter.</param>
        /// <param name="destinationType">One of the types supported by this converter to which the <paramref name="sourceValue"/> parameter should be converted to.</param>
        /// <param name="formatProvider">The format provider to use like in IFormattable's ToString.</param>
        /// <param name="ignoreCase">True if case should be ignored.</param>
        /// <returns>The <paramref name="sourceValue"/> parameter converted to the <paramref name="destinationType"/> parameter using formatProvider and ignoreCase.</returns>
        /// <exception cref="InvalidCastException">If no conversion was possible.</exception>
        public virtual object ConvertFrom(
            PSObject sourceValue,
            Type destinationType,
            IFormatProvider formatProvider,
            bool ignoreCase)
                => ConvertFrom(GetSourceValueAsObject(sourceValue), destinationType, formatProvider, ignoreCase);

        /// <summary>
        /// Returns true if the converter can convert the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter.
        /// </summary>
        /// <param name="sourceValue">Value supposedly from one of the types supported by this converter to be converted to the <paramref name="destinationType"/> parameter.</param>
        /// <param name="destinationType">Type to convert the <paramref name="sourceValue"/> parameter, supposedly not one of the types supported by the converter.</param>
        /// <returns>True if the converter can convert the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter, otherwise false.</returns>
        public abstract bool CanConvertTo(object sourceValue, Type destinationType);

        /// <summary>
        /// Returns true if the converter can convert the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter.
        /// </summary>
        /// <param name="sourceValue">Value supposedly from one of the types supported by this converter to be converted to the <paramref name="destinationType"/> parameter.</param>
        /// <param name="destinationType">Type to convert the <paramref name="sourceValue"/> parameter, supposedly not one of the types supported by the converter.</param>
        /// <returns>True if the converter can convert the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter, otherwise false.</returns>
        public virtual bool CanConvertTo(PSObject sourceValue, Type destinationType)
            => CanConvertTo(GetSourceValueAsObject(sourceValue), destinationType);

        /// <summary>
        /// Converts the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter using formatProvider and ignoreCase.
        /// </summary>
        /// <param name="sourceValue">Value supposedly from one of the types supported by this converter to be converted to the <paramref name="destinationType"/> parameter.</param>
        /// <param name="destinationType">Type to convert the <paramref name="sourceValue"/> parameter, supposedly not one of the types supported by the converter.</param>
        /// <param name="formatProvider">The format provider to use like in IFormattable's ToString.</param>
        /// <param name="ignoreCase">True if case should be ignored.</param>
        /// <returns>SourceValue converted to the <paramref name="destinationType"/> parameter using formatProvider and ignoreCase.</returns>
        /// <exception cref="InvalidCastException">If no conversion was possible.</exception>
        public abstract object ConvertTo(
            object sourceValue,
            Type destinationType,
            IFormatProvider formatProvider,
            bool ignoreCase);

        /// <summary>
        /// Converts the <paramref name="sourceValue"/> parameter to the <paramref name="destinationType"/> parameter using formatProvider and ignoreCase.
        /// </summary>
        /// <param name="sourceValue">Value supposedly from one of the types supported by this converter to be converted to the <paramref name="destinationType"/> parameter.</param>
        /// <param name="destinationType">Type to convert the <paramref name="sourceValue"/> parameter, supposedly not one of the types supported by the converter.</param>
        /// <param name="formatProvider">The format provider to use like in IFormattable's ToString.</param>
        /// <param name="ignoreCase">True if case should be ignored.</param>
        /// <returns>SourceValue converted to the <paramref name="destinationType"/> parameter using formatProvider and ignoreCase.</returns>
        /// <exception cref="InvalidCastException">If no conversion was possible.</exception>
        public virtual object ConvertTo(
            PSObject sourceValue,
            Type destinationType,
            IFormatProvider formatProvider,
            bool ignoreCase)
                => ConvertTo(GetSourceValueAsObject(sourceValue), destinationType, formatProvider, ignoreCase);
    }

    /// <summary>
    /// Enables a type that only has conversion from string to be converted from all other
    /// types through string.
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="ConvertThroughString"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class ConvertThroughString : PSTypeConverter
    {
        /// <summary>
        /// This will return false only if sourceValue is string, to avoid recursion.
        /// </summary>
        /// <param name="sourceValue">Value to convert from.</param>
        /// <param name="destinationType">Ignored.</param>
        /// <returns>False only if sourceValue is string.</returns>
        public override bool CanConvertFrom(object sourceValue, Type destinationType) => !(sourceValue is string);

        /// <summary>
        /// Converts to destinationType by first converting sourceValue to string
        /// and then converting the result to destinationType.
        /// </summary>
        /// <param name="sourceValue">The value to convert from.</param>
        /// <param name="destinationType">The type this converter is associated with.</param>
        /// <param name="formatProvider">The IFormatProvider to use.</param>
        /// <param name="ignoreCase">True if case should be ignored.</param>
        /// <returns>SourceValue converted to destinationType.</returns>
        /// <exception cref="PSInvalidCastException">When no conversion was possible.</exception>
        public override object ConvertFrom(
            object sourceValue,
            Type destinationType,
            IFormatProvider formatProvider,
            bool ignoreCase)
        {
            string sourceAsString = (string)LanguagePrimitives.ConvertTo(sourceValue, typeof(string), formatProvider);
            return LanguagePrimitives.ConvertTo(sourceAsString, destinationType, formatProvider);
        }

        /// <summary>
        /// Returns false, since this converter is not designed to be used to
        /// convert from the type associated with the converted to other types.
        /// </summary>
        /// <param name="sourceValue">The value to convert from.</param>
        /// <param name="destinationType">The value to convert from.</param>
        /// <returns>False.</returns>
        public override bool CanConvertTo(object sourceValue, Type destinationType) => false;

        /// <summary>
        /// Throws NotSupportedException, since this converter is not designed to be used to
        /// convert from the type associated with the converted to other types.
        /// </summary>
        /// <param name="sourceValue">The value to convert from.</param>
        /// <param name="destinationType">The value to convert from.</param>
        /// <param name="formatProvider">The IFormatProvider to use.</param>
        /// <param name="ignoreCase">True if case should be ignored.</param>
        /// <returns>This method does not return a value.</returns>
        /// <exception cref="NotSupportedException">NotSupportedException is always thrown.</exception>
        public override object ConvertTo(
            object sourceValue,
            Type destinationType,
            IFormatProvider formatProvider,
            bool ignoreCase)
                => throw PSTraceSource.NewNotSupportedException();
    }
    #endregion public type converters

    /// <summary>
    /// The ranking of versions for comparison purposes (used in overload resolution.)
    /// A larger value means the conversion is better.
    ///
    /// Note that the lower nibble is all ones for named conversion ranks.  This allows for
    /// conversions with rankings in between the named values.  For example, int=>string[]
    /// is value dependent, if the conversion from int=>string succeeds, then an array is
    /// created, otherwise we try some other conversion.  The int=>string[] conversion should
    /// be worse than int=>string, but it is probably better than many other conversions, so
    /// we want it to be only slightly worse than int=>string.
    /// </summary>
    /// <remarks>
    /// ValueDependent is a flag, but we don't mark the enum as flags because it really isn't
    /// a flags enum.
    /// To make debugging easier, the "in between" conversions are all named, though there are
    /// no references in the code.  They all use the suffix S2A which means "scalar to array".
    /// </remarks>
    internal enum ConversionRank
    {
        None = 0x0000,
        UnrelatedArraysS2A = 0x0007,
        UnrelatedArrays = 0x000F,
        ToStringS2A = 0x0017,
        ToString = 0x001F,
        CustomS2A = 0x0027,
        Custom = 0x002F,
        IConvertibleS2A = 0x0037,
        IConvertible = 0x003F,
        ImplicitCastS2A = 0x0047,
        ImplicitCast = 0x004F,
        ExplicitCastS2A = 0x0057,
        ExplicitCast = 0x005F,
        ConstructorS2A = 0x0067,
        Constructor = 0x006F,
        Create = 0x0073,
        ParseS2A = 0x0077,
        Parse = 0x007F,
        PSObjectS2A = 0x0087,
        PSObject = 0x008F,
        LanguageS2A = 0x0097,
        Language = 0x009F,
        NullToValue = 0x00AF,
        NullToRef = 0x00BF,
        NumericExplicitS2A = 0x00C7,
        NumericExplicit = 0x00CF,
        NumericExplicit1S2A = 0x00D7,
        NumericExplicit1 = 0x00DF,
        NumericStringS2A = 0x00E7,
        NumericString = 0x00EF,
        NumericImplicitS2A = 0x00F7,
        NumericImplicit = 0x00FF,
        AssignableS2A = 0x0107,
        Assignable = 0x010F,
        IdentityS2A = 0x0117,
        StringToCharArray = 0x011A,
        Identity = 0x011F,

        ValueDependent = 0xFFF7,
    }

    /// <summary>
    /// Defines language support methods.
    /// </summary>
    [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling",
        Justification = "Refactoring LanguagePrimitives takes lot of dev/test effort. Since V1 code is already shipped, we tend to exclude this message.")]
    public static class LanguagePrimitives
    {
        [TraceSource("ETS", "Extended Type System")]
        private static readonly PSTraceSource s_tracer = PSTraceSource.GetTracer("ETS", "Extended Type System");

        internal delegate void MemberNotFoundError(PSObject pso, DictionaryEntry property, Type resultType);

        internal delegate void MemberSetValueError(SetValueException e);

        internal const string OrderedAttribute = "ordered";
        internal const string DoublePrecision = "G15";
        internal const string SinglePrecision = "G7";

        internal static void CreateMemberNotFoundError(PSObject pso, DictionaryEntry property, Type resultType)
        {
            string availableProperties = GetAvailableProperties(pso);

            string message = StringUtil.Format(
                ExtendedTypeSystem.PropertyNotFound,
                property.Key.ToString(),
                resultType.FullName,
                availableProperties);

            typeConversion.WriteLine("Issuing an error message about not being able to create an object from hashtable.");
            throw new InvalidOperationException(message);
        }

        internal static void CreateMemberSetValueError(SetValueException e)
        {
            typeConversion.WriteLine("Issuing an error message about not being able to set the properties for an object.");
            throw e;
        }

        static LanguagePrimitives()
        {
            RebuildConversionCache();
            InitializeGetEnumerableCache();
        }

        internal static void UpdateTypeConvertFromTypeTable(string typeName)
        {
            lock (s_converterCache)
            {
                ConversionTypePair[] toRemove = s_converterCache.Keys.Where(
                    pair => string.Equals(pair.to.FullName, typeName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(pair.from.FullName, typeName, StringComparison.OrdinalIgnoreCase)).ToArray();

                foreach (ConversionTypePair pair in toRemove)
                {
                    s_converterCache.Remove(pair);
                }

                // Note we do not clear possibleTypeConverter even when removing.
                //
                // The conversion cache and the possibleTypeConverter cache are process wide, but
                // the type table used for any specific conversion is runspace specific.
                s_possibleTypeConverter[typeName] = true;
            }
        }

        #region GetEnumerable/GetEnumerator

        /// <summary>
        /// This is a wrapper class that allows us to use the generic IEnumerable
        /// implementation of an object when we can't use it's non-generic
        /// implementation.
        /// </summary>
        private class EnumerableTWrapper : IEnumerable
        {
            private readonly object _enumerable;
            private readonly Type _enumerableType;
            private DynamicMethod _getEnumerator;

            internal EnumerableTWrapper(object enumerable, Type enumerableType)
            {
                _enumerable = enumerable;
                _enumerableType = enumerableType;
                CreateGetEnumerator();
            }

            private void CreateGetEnumerator()
            {
                _getEnumerator = new DynamicMethod(
                    name: "GetEnumerator",
                    returnType: typeof(object),
                    parameterTypes: new Type[] { typeof(object) },
                    typeof(LanguagePrimitives).Module,
                    skipVisibility: true);

                ILGenerator emitter = _getEnumerator.GetILGenerator();

                emitter.Emit(OpCodes.Ldarg_0);
                emitter.Emit(OpCodes.Castclass, _enumerableType);
                MethodInfo methodInfo = _enumerableType.GetMethod("GetEnumerator", new Type[] { });
                emitter.Emit(OpCodes.Callvirt, methodInfo);
                emitter.Emit(OpCodes.Ret);
            }

            #region IEnumerable Members

            public IEnumerator GetEnumerator()
                => (IEnumerator)_getEnumerator.Invoke(null, new object[] { _enumerable });

            #endregion
        }

        private static IEnumerable GetEnumerableFromIEnumerableT(object obj)
        {
            foreach (Type type in obj.GetType().GetInterfaces())
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return new EnumerableTWrapper(obj, type);
                }
            }

            return null;
        }

        private delegate IEnumerable GetEnumerableDelegate(object obj);
        private static readonly Dictionary<Type, GetEnumerableDelegate> s_getEnumerableCache =
            new Dictionary<Type, GetEnumerableDelegate>(32);

        private static GetEnumerableDelegate GetOrCalculateEnumerable(Type type)
        {
            GetEnumerableDelegate getEnumerable = null;
            lock (s_getEnumerableCache)
            {
                if (!s_getEnumerableCache.TryGetValue(type, out getEnumerable))
                {
                    getEnumerable = CalculateGetEnumerable(type);
                    s_getEnumerableCache.Add(type, getEnumerable);
                }
            }

            return getEnumerable;
        }

        private static void InitializeGetEnumerableCache()
        {
            lock (s_getEnumerableCache)
            {
                // PowerShell doesn't treat strings as enumerables so just return null.
                // we also want to return null on common numeric types very quickly
                s_getEnumerableCache.Clear();
                s_getEnumerableCache.Add(typeof(string), ReturnNullEnumerable);
                s_getEnumerableCache.Add(typeof(int), ReturnNullEnumerable);
                s_getEnumerableCache.Add(typeof(double), ReturnNullEnumerable);
            }
        }

        internal static bool IsTypeEnumerable(Type type)
            => type != null && GetOrCalculateEnumerable(type) != ReturnNullEnumerable;

        /// <summary>
        /// Returns True if the language considers obj to be IEnumerable.
        /// </summary>
        /// <param name="obj">
        /// IEnumerable or IEnumerable-like object
        /// </param>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "obj",
            Justification = "Since V1 code is already shipped, excluding this message.")]
        public static bool IsObjectEnumerable(object obj)
            => IsTypeEnumerable(PSObject.Base(obj)?.GetType());

        /// <summary>
        /// Retrieves the IEnumerable of obj or null if the language does not consider obj to be IEnumerable.
        /// </summary>
        /// <param name="obj">
        /// IEnumerable or IEnumerable-like object
        /// </param>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "obj",
            Justification = "Since V1 code is already shipped, excluding this message.")]
        public static IEnumerable GetEnumerable(object obj)
        {
            obj = PSObject.Base(obj);
            if (obj == null)
            {
                return null;
            }

            GetEnumerableDelegate getEnumerable = GetOrCalculateEnumerable(obj.GetType());
            return getEnumerable(obj);
        }

        private static IEnumerable ReturnNullEnumerable(object obj) => null;

        private static IEnumerable DataTableEnumerable(object obj) => ((DataTable)obj).Rows;

        private static IEnumerable TypicalEnumerable(object obj)
        {
            IEnumerable e = (IEnumerable)obj;
            try
            {
                // Some IEnumerable implementations just return null.  Others
                // raise an exception.  Either of these may have a perfectly
                // good generic implementation, so we'll try those if the
                // non-generic is no good.
                if (e.GetEnumerator() == null)
                {
                    return GetEnumerableFromIEnumerableT(obj);
                }

                return e;
            }
            catch (Exception innerException)
            {
                e = GetEnumerableFromIEnumerableT(obj);
                if (e != null)
                {
                    return e;
                }

                throw new ExtendedTypeSystemException(
                    "ExceptionInGetEnumerator",
                    innerException,
                    ExtendedTypeSystem.EnumerationException,
                    innerException.Message);
            }
        }

        private static GetEnumerableDelegate CalculateGetEnumerable(Type objectType)
        {
            if (typeof(DataTable).IsAssignableFrom(objectType))
            {
                return DataTableEnumerable;
            }

            // Don't treat IDictionary or XmlNode as enumerable...
            if (typeof(IEnumerable).IsAssignableFrom(objectType)
                && !typeof(IDictionary).IsAssignableFrom(objectType)
                && !typeof(XmlNode).IsAssignableFrom(objectType))
            {
                return TypicalEnumerable;
            }

            return ReturnNullEnumerable;
        }

        private static readonly CallSite<Func<CallSite, object, IEnumerator>> s_getEnumeratorSite =
            CallSite<Func<CallSite, object, IEnumerator>>.Create(PSEnumerableBinder.Get());

        /// <summary>
        /// Retrieves the IEnumerator of obj or null if the language does not consider obj as capable of returning an IEnumerator.
        /// </summary>
        /// <param name="obj">
        /// IEnumerable or IEnumerable-like object
        /// </param>
        /// <exception cref="ExtendedTypeSystemException">When the act of getting the enumerator throws an exception.</exception>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "obj",
            Justification = "Since V1 code is already shipped, excluding this message for backward compatibility reasons.")]
        public static IEnumerator GetEnumerator(object obj)
        {
            IEnumerator result = s_getEnumeratorSite.Target.Invoke(s_getEnumeratorSite, obj);
            return result is EnumerableOps.NonEnumerableObjectEnumerator ? null : result;
        }

        #endregion GetEnumerable/GetEnumerator

        /// <summary>
        /// This method takes a an arbitrary object and wraps it in a PSDataCollection of PSObject.
        /// This simplifies interacting with the PowerShell workflow activities.
        /// </summary>
        /// <param name="inputValue"></param>
        /// <returns></returns>
        public static PSDataCollection<PSObject> GetPSDataCollection(object inputValue)
        {
            PSDataCollection<PSObject> result = new PSDataCollection<PSObject>();
            if (inputValue != null)
            {
                IEnumerator enumerator = GetEnumerator(inputValue);
                if (enumerator != null)
                {
                    while (enumerator.MoveNext())
                    {
                        result.Add(enumerator.Current == null
                            ? null
                            : PSObject.AsPSObject(enumerator.Current));
                    }
                }
                else
                {
                    result.Add(PSObject.AsPSObject(inputValue));
                }
            }

            result.Complete();
            return result;
        }

        /// <summary>
        /// Used to compare two objects for equality converting the second to the type of the first, if required.
        /// </summary>
        /// <param name="first">First object.</param>
        /// <param name="second">Object to compare first to.</param>
        /// <returns>True if first is equal to the second.</returns>
        public static new bool Equals(object first, object second)
            => Equals(first, second, false, CultureInfo.InvariantCulture);

        /// <summary>
        /// Used to compare two objects for equality converting the second to the type of the first, if required.
        /// </summary>
        /// <param name="first">First object.</param>
        /// <param name="second">Object to compare first to.</param>
        /// <param name="ignoreCase">used only if first and second are strings
        /// to specify the type of string comparison </param>
        /// <returns>True if first is equal to the second.</returns>
        public static bool Equals(object first, object second, bool ignoreCase)
             => Equals(first, second, ignoreCase, CultureInfo.InvariantCulture);

        /// <summary>
        /// Used to compare two objects for equality converting the second to the type of the first, if required.
        /// </summary>
        /// <param name="first">First object.</param>
        /// <param name="second">Object to compare first to.</param>
        /// <param name="ignoreCase">used only if first and second are strings
        /// to specify the type of string comparison </param>
        /// <param name="formatProvider">the format/culture to be used. If this parameter is null,
        /// CultureInfo.InvariantCulture will be used.
        /// </param>
        /// <returns>True if first is equal to the second.</returns>
        public static bool Equals(object first, object second, bool ignoreCase, IFormatProvider formatProvider)
        {
            // If both first and second are null it returns true.
            // If one is null and the other is not it returns false.
            // if (first.Equals(second)) it returns true otherwise it goes ahead with type conversion operations.
            // If both first and second are strings it returns (string.Compare(firstString, secondString, ignoreCase) == 0).
            // If second can be converted to the type of the first, it does so and returns first.Equals(secondConverted)
            // Otherwise false is returned
            formatProvider ??= CultureInfo.InvariantCulture;

            var culture = formatProvider as CultureInfo;
            if (culture == null)
            {
                throw PSTraceSource.NewArgumentException("formatProvider");
            }

            first = PSObject.Base(first);
            second = PSObject.Base(second);

            if (first == null)
            {
                return second == null;
            }

            if (second == null)
            {
                return false; // first is not null
            }

            if (first is string firstString)
            {
                string secondString = second as string ?? (string)ConvertTo(second, typeof(string), culture);
                return (culture.CompareInfo.Compare(
                    firstString,
                    secondString,
                    ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None) == 0);
            }

            if (first.Equals(second))
            {
                return true;
            }

            Type firstType = first.GetType();
            Type secondType = second.GetType();
            int firstIndex = TypeTableIndex(firstType);
            int secondIndex = TypeTableIndex(secondType);

            if (firstIndex != -1 && secondIndex != -1)
            {
                return NumericCompare(first, second, firstIndex, secondIndex) == 0;
            }

            if (firstType == typeof(char) && ignoreCase)
            {
                if (second is string secondString && secondString.Length == 1)
                {
                    char firstAsUpper = culture.TextInfo.ToUpper((char)first);
                    char secondAsUpper = culture.TextInfo.ToUpper(secondString[0]);

                    return firstAsUpper.Equals(secondAsUpper);
                }
                else if (secondType == typeof(char))
                {
                    char firstAsUpper = culture.TextInfo.ToUpper((char)first);
                    char secondAsUpper = culture.TextInfo.ToUpper((char)second);

                    return firstAsUpper.Equals(secondAsUpper);
                }
            }

            try
            {
                object secondConverted = ConvertTo(second, firstType, culture);
                return first.Equals(secondConverted);
            }
            catch (InvalidCastException)
            {
            }

            return false;
        }

        /// <summary>
        /// Helper method for [Try]Compare to determine object ordering with null.
        /// </summary>
        /// <param name="value">The numeric value to compare to null.</param>
        /// <param name="numberIsRightHandSide">True if the number to compare is on the right hand side if the comparison.</param>
        private static int CompareObjectToNull(object value, bool numberIsRightHandSide)
        {
            var order = numberIsRightHandSide ? -1 : 1;

            // If it's a positive number, including 0, it's greater than null
            // for everything else it's less than zero...
            return value switch
            {
                short s => Math.Sign(s) * order,
                int i32 => Math.Sign(i32) * order,
                long l => Math.Sign(l) * order,
                sbyte sby => Math.Sign(sby) * order,
                float f => Math.Sign(f) * order,
                double d => Math.Sign(d) * order,
                decimal de => Math.Sign(de) * order,
                _ => order
            };
        }

        /// <summary>
        /// Compare first and second, converting second to the
        /// type of the first, if necessary.
        /// </summary>
        /// <param name="first">First comparison value.</param>
        /// <param name="second">Second comparison value.</param>
        /// <returns>Less than zero if first is smaller than second, more than
        /// zero if it is greater or zero if they are the same.</returns>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="first"/> does not implement IComparable or <paramref name="second"/> cannot be converted
        /// to the type of <paramref name="first"/>.
        /// </exception>
        public static int Compare(object first, object second)
            => Compare(first, second, ignoreCase: false, CultureInfo.InvariantCulture);

        /// <summary>
        /// Compare first and second, converting second to the
        /// type of the first, if necessary.
        /// </summary>
        /// <param name="first">First comparison value.</param>
        /// <param name="second">Second comparison value.</param>
        /// <param name="ignoreCase">Used if both values are strings.</param>
        /// <returns>Less than zero if first is smaller than second, more than
        /// zero if it is greater or zero if they are the same.</returns>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="first"/> does not implement IComparable or <paramref name="second"/> cannot be converted
        /// to the type of <paramref name="first"/>.
        /// </exception>
        public static int Compare(object first, object second, bool ignoreCase)
            => Compare(first, second, ignoreCase, CultureInfo.InvariantCulture);

        /// <summary>
        /// Compare first and second, converting second to the
        /// type of the first, if necessary.
        /// </summary>
        /// <param name="first">First comparison value.</param>
        /// <param name="second">Second comparison value.</param>
        /// <param name="ignoreCase">Used if both values are strings.</param>
        /// <param name="formatProvider">Used in type conversions and if both values are strings.</param>
        /// <returns>Less than zero if first is smaller than second, more than
        /// zero if it is greater or zero if they are the same.</returns>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="first"/> does not implement IComparable or <paramref name="second"/> cannot be converted
        /// to the type of <paramref name="first"/>.
        /// </exception>
        public static int Compare(object first, object second, bool ignoreCase, IFormatProvider formatProvider)
        {
            formatProvider ??= CultureInfo.InvariantCulture;

            var culture = formatProvider as CultureInfo;
            if (culture == null)
            {
                throw PSTraceSource.NewArgumentException("formatProvider");
            }

            first = PSObject.Base(first);
            second = PSObject.Base(second);

            if (first == null)
            {
                return second == null ? 0 : CompareObjectToNull(second, true);
            }

            if (second == null)
            {
                return CompareObjectToNull(first, false);
            }

            if (first is string firstString)
            {
                string secondString = second as string;
                if (secondString == null)
                {
                    try
                    {
                        secondString = (string)ConvertTo(second, typeof(string), culture);
                    }
                    catch (PSInvalidCastException e)
                    {
                        throw PSTraceSource.NewArgumentException(
                            nameof(second),
                            ExtendedTypeSystem.ComparisonFailure,
                            first.ToString(),
                            second.ToString(),
                            e.Message);
                    }
                }

                return culture.CompareInfo.Compare(
                    firstString,
                    secondString,
                    ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None);
            }

            Type firstType = first.GetType();
            Type secondType = second.GetType();
            int firstIndex = TypeTableIndex(firstType);
            int secondIndex = TypeTableIndex(secondType);
            if ((firstIndex != -1) && (secondIndex != -1))
            {
                return NumericCompare(first, second, firstIndex, secondIndex);
            }

            object secondConverted;
            try
            {
                secondConverted = ConvertTo(second, firstType, culture);
            }
            catch (PSInvalidCastException e)
            {
                throw PSTraceSource.NewArgumentException(
                    nameof(second),
                    ExtendedTypeSystem.ComparisonFailure,
                    first.ToString(),
                    second.ToString(),
                    e.Message);
            }

            if (first is IComparable firstComparable)
            {
                return firstComparable.CompareTo(secondConverted);
            }

            if (first.Equals(second))
            {
                return 0;
            }

            // At this point, we know that they aren't equal but we have no way of
            // knowing which should compare greater than the other so we throw an exception.
            throw PSTraceSource.NewArgumentException(nameof(first), ExtendedTypeSystem.NotIcomparable, first.ToString());
        }

        /// <summary>
        /// Tries to compare first and second, converting second to the type of the first, if necessary.
        /// If a conversion is needed but fails, false is return.
        /// </summary>
        /// <param name="first">First comparison value.</param>
        /// <param name="second">Second comparison value.</param>
        /// <param name="result">Less than zero if first is smaller than second, more than
        /// zero if it is greater or zero if they are the same.</param>
        /// <returns>True if the comparison was successful, false otherwise.</returns>
        public static bool TryCompare(object first, object second, out int result)
            => TryCompare(first, second, ignoreCase: false, CultureInfo.InvariantCulture, out result);

        /// <summary>
        /// Tries to compare first and second, converting second to the type of the first, if necessary.
        /// If a conversion is needed but fails, false is return.
        /// </summary>
        /// <param name="first">First comparison value.</param>
        /// <param name="second">Second comparison value.</param>
        /// <param name="ignoreCase">Used if both values are strings.</param>
        /// <param name="result">Less than zero if first is smaller than second, more than zero if it is greater or zero if they are the same.</param>
        /// <returns>True if the comparison was successful, false otherwise.</returns>
        public static bool TryCompare(object first, object second, bool ignoreCase, out int result)
            => TryCompare(first, second, ignoreCase, CultureInfo.InvariantCulture, out result);

        /// <summary>
        /// Tries to compare first and second, converting second to the type of the first, if necessary.
        /// If a conversion is needed but fails, false is return.
        /// </summary>
        /// <param name="first">First comparison value.</param>
        /// <param name="second">Second comparison value.</param>
        /// <param name="ignoreCase">Used if both values are strings.</param>
        /// <param name="formatProvider">Used in type conversions and if both values are strings.</param>
        /// <param name="result">Less than zero if first is smaller than second, more than  zero if it is greater or zero if they are the same.</param>
        /// <returns>True if the comparison was successful, false otherwise.</returns>
        /// <exception cref="ArgumentException">The parameter <paramref name="formatProvider"/> is not a <see cref="CultureInfo"/>.</exception>
        public static bool TryCompare(
            object first,
            object second,
            bool ignoreCase,
            IFormatProvider formatProvider,
            out int result)
        {
            result = 0;
            formatProvider ??= CultureInfo.InvariantCulture;

            if (!(formatProvider is CultureInfo culture))
            {
                throw PSTraceSource.NewArgumentException("formatProvider");
            }

            first = PSObject.Base(first);
            second = PSObject.Base(second);

            if (first == null && second == null)
            {
                result = 0;
                return true;
            }

            if (first == null)
            {
                result = CompareObjectToNull(second, true);
                return true;
            }

            if (second == null)
            {
                // If it's a positive number, including 0, it's greater than null
                // for everything else it's less than zero...
                result = CompareObjectToNull(first, false);
                return true;
            }

            if (first is string firstString)
            {
                if (!(second is string secondString))
                {
                    if (!TryConvertTo(second, culture, out secondString))
                    {
                        return false;
                    }
                }

                result = culture.CompareInfo.Compare(firstString, secondString, ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None);
                return true;
            }

            Type firstType = first.GetType();
            Type secondType = second.GetType();
            int firstIndex = TypeTableIndex(firstType);
            int secondIndex = TypeTableIndex(secondType);
            if (firstIndex != -1 && secondIndex != -1)
            {
                result = NumericCompare(first, second, firstIndex, secondIndex);
                return true;
            }

            if (!TryConvertTo(second, firstType, culture, out object secondConverted))
            {
                return false;
            }

            if (first is IComparable firstComparable)
            {
                result = firstComparable.CompareTo(secondConverted);
                return true;
            }

            if (first.Equals(second))
            {
                result = 0;
                return true;
            }

            // At this point, we know that they aren't equal but we have no way of
            // knowing which should compare greater than the other so we return false.
            return false;
        }

        /// <summary>
        /// Returns true if the language considers obj to be true.
        /// </summary>
        /// <param name="obj">Obj to verify if it is true.</param>
        /// <returns>True if obj is true.</returns>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "obj",
            Justification = "Since V1 code is already shipped, excluding this message for backward compatibility reasons")]
        public static bool IsTrue(object obj)
        {
            // null is a valid argument - it converts to false...
            if (IsNull(obj))
            {
                return false;
            }

            obj = PSObject.Base(obj);

            Type objType = obj.GetType();

            if (objType == typeof(bool))
            {
                return (bool)obj;
            }

            if (objType == typeof(string))
            {
                return IsTrue((string)obj);
            }

            if (IsNumeric(GetTypeCode(objType)))
            {
                IConversionData data = GetConversionData(objType, typeof(bool))
                    ?? CacheConversion(
                        objType,
                        typeof(bool),
                        CreateNumericToBoolConverter(objType),
                        ConversionRank.Language);

                return (bool)data.Invoke(
                    valueToConvert: obj,
                    resultType: typeof(bool),
                    recurse: false,
                    originalValueToConvert: null,
                    formatProvider: null,
                    backupTable: null);
            }

            if (objType == typeof(SwitchParameter))
            {
                return ((SwitchParameter)obj).ToBool();
            }

            if (obj is IList objectArray)
            {
                return IsTrue(objectArray);
            }

            return true;
        }

        internal static bool IsTrue(string s)
            => s.Length != 0;

        internal static bool IsTrue(IList objectArray)
        {
            switch (objectArray.Count)
            {
                // a zero length array is false, so condition is false
                case 0:
                    return false;

                // if the result is an array of length 1, treat it as a scalar...
                case 1:
                    // A possible implementation would be just
                    // return IsTrue(objectArray[0]);
                    // but since we don't want this to recurse indefinitely
                    // we explicitly check the case where it would recurse
                    // and deal with it.
                    IList firstElement = PSObject.Base(objectArray[0]) as IList;

                    if (firstElement == null)
                    {
                        return IsTrue(objectArray[0]);
                    }

                    if (firstElement.Count < 1)
                    {
                        return false;
                    }

                    // the first element is an array with more than zero elements
                    return true;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Internal routine that determines if an object meets any of our criteria for null.
        /// </summary>
        /// <param name="obj">The object to test.</param>
        /// <returns>True if the object is null.</returns>
        internal static bool IsNull(object obj)
            => (obj == null || obj == AutomationNull.Value);

        /// <summary>
        /// Auxiliary for the cases where we want a new PSObject or null.
        /// </summary>
        internal static PSObject AsPSObjectOrNull(object obj)
            => obj != null ? PSObject.AsPSObject(obj) : null;

        internal static int TypeTableIndex(Type type)
            => GetTypeCode(type) switch
            {
                TypeCode.Int16 => 0,
                TypeCode.Int32 => 1,
                TypeCode.Int64 => 2,
                TypeCode.UInt16 => 3,
                TypeCode.UInt32 => 4,
                TypeCode.UInt64 => 5,
                TypeCode.SByte => 6,
                TypeCode.Byte => 7,
                TypeCode.Single => 8,
                TypeCode.Double => 9,
                TypeCode.Decimal => 10,
                _ => -1
            };

        /// <summary>
        /// Table of the largest safe type to which both types can be converted without exceptions.
        /// This table is used for numeric comparisons.
        /// The 4 entries marked as not used, are explicitly dealt with in NumericCompareDecimal.
        /// NumericCompareDecimal exists because doubles and singles can throw
        /// an exception when converted to decimal.
        /// The order of lines and columns cannot be changed since NumericCompare depends on it.
        /// </summary>
        internal static Type[][] LargestTypeTable = new Type[][]
        {
            //                                       System.Int16            System.Int32            System.Int64            System.UInt16           System.UInt32           System.UInt64           System.SByte            System.Byte             System.Single           System.Double           System.Decimal
            /* System.Int16   */new Type[] { typeof(System.Int16),   typeof(System.Int32),   typeof(System.Int64),   typeof(System.Int32),   typeof(System.Int64),   typeof(System.Double),  typeof(System.Int16),   typeof(System.Int16),   typeof(System.Single),  typeof(System.Double),  typeof(System.Decimal) },
            /* System.Int32   */new Type[] { typeof(System.Int32),   typeof(System.Int32),   typeof(System.Int64),   typeof(System.Int32),   typeof(System.Int64),   typeof(System.Double),  typeof(System.Int32),   typeof(System.Int32),   typeof(System.Double),  typeof(System.Double),  typeof(System.Decimal) },
            /* System.Int64   */new Type[] { typeof(System.Int64),   typeof(System.Int64),   typeof(System.Int64),   typeof(System.Int64),   typeof(System.Int64),   typeof(System.Decimal), typeof(System.Int64),   typeof(System.Int64),   typeof(System.Double),  typeof(System.Double),  typeof(System.Decimal) },
            /* System.UInt16  */new Type[] { typeof(System.Int32),   typeof(System.Int32),   typeof(System.Int64),   typeof(System.UInt16),  typeof(System.UInt32),  typeof(System.UInt64),  typeof(System.Int32),   typeof(System.UInt16),  typeof(System.Single),  typeof(System.Double),  typeof(System.Decimal) },
            /* System.UInt32  */new Type[] { typeof(System.Int64),   typeof(System.Int64),   typeof(System.Int64),   typeof(System.UInt32),  typeof(System.UInt32),  typeof(System.UInt64),  typeof(System.Int64),   typeof(System.UInt32),  typeof(System.Double),  typeof(System.Double),  typeof(System.Decimal) },
            /* System.UInt64  */new Type[] { typeof(System.Double),  typeof(System.Double),  typeof(System.Decimal), typeof(System.UInt64),  typeof(System.UInt64),  typeof(System.UInt64),  typeof(System.Double),  typeof(System.UInt64),  typeof(System.Double),  typeof(System.Double),  typeof(System.Decimal) },
            /* System.SByte   */new Type[] { typeof(System.Int16),   typeof(System.Int32),   typeof(System.Int64),   typeof(System.Int32),   typeof(System.Int64),   typeof(System.Double),  typeof(System.SByte),   typeof(System.Int16),   typeof(System.Single),  typeof(System.Double),  typeof(System.Decimal) },
            /* System.Byte    */new Type[] { typeof(System.Int16),   typeof(System.Int32),   typeof(System.Int64),   typeof(System.UInt16),  typeof(System.UInt32),  typeof(System.UInt64),  typeof(System.Int16),   typeof(System.Byte),    typeof(System.Single),  typeof(System.Double),  typeof(System.Decimal) },
            /* System.Single  */new Type[] { typeof(System.Single),  typeof(System.Double),  typeof(System.Double),  typeof(System.Single),  typeof(System.Double),  typeof(System.Double),  typeof(System.Single),  typeof(System.Single),  typeof(System.Single),  typeof(System.Double),  null/*not used*/       },
            /* System.Double  */new Type[] { typeof(System.Double),  typeof(System.Double),  typeof(System.Double),  typeof(System.Double),  typeof(System.Double),  typeof(System.Double),  typeof(System.Double),  typeof(System.Double),  typeof(System.Double),  typeof(System.Double),  null/*not used*/       },
            /* System.Decimal */new Type[] { typeof(System.Decimal), typeof(System.Decimal), typeof(System.Decimal), typeof(System.Decimal), typeof(System.Decimal), typeof(System.Decimal), typeof(System.Decimal), typeof(System.Decimal), null/*not used*/,       null/*not used*/,       typeof(System.Decimal) },
        };

        private static int NumericCompareDecimal(decimal decimalNumber, object otherNumber)
        {
            object otherDecimal;
            try
            {
                otherDecimal = Convert.ChangeType(otherNumber, typeof(decimal), CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                try
                {
                    double wasDecimal = (double)Convert.ChangeType(decimalNumber, typeof(double), CultureInfo.InvariantCulture);
                    double otherDouble = (double)Convert.ChangeType(otherNumber, typeof(double), CultureInfo.InvariantCulture);
                    return ((IComparable)wasDecimal).CompareTo(otherDouble);
                }
                catch (Exception) // We need to catch the generic exception because ChangeType throws unadvertised exceptions
                {
                    return -1;
                }
            }
            catch (Exception) // We need to catch the generic exception because ChangeType throws unadvertised exceptions
            {
                return -1;
            }

            return ((IComparable)decimalNumber).CompareTo(otherDecimal);
        }

        private static int NumericCompare(object number1, object number2, int index1, int index2)
        {
            // Conversion from single or double to decimal might throw
            // if the double is greater than the decimal's maximum so
            // we special case it in NumericCompareDecimal
            if (index1 == 10 && (index2 == 8 || index2 == 9))
            {
                return NumericCompareDecimal((decimal)number1, number2);
            }

            if (index2 == 10 && (index1 == 8 || index1 == 9))
            {
                return -NumericCompareDecimal((decimal)number2, number1);
            }

            Type commonType = LargestTypeTable[index1][index2];
            object number1Converted = Convert.ChangeType(number1, commonType, CultureInfo.InvariantCulture);
            object number2Converted = Convert.ChangeType(number2, commonType, CultureInfo.InvariantCulture);
            return ((IComparable)number1Converted).CompareTo(number2Converted);
        }

        /// <summary>
        /// Necessary not to return an integer type code for enums.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        internal static TypeCode GetTypeCode(Type type)
            => type.GetTypeCode();

        /// <summary>
        /// Emulates the "As" C# language primitive, but will unwrap
        /// the PSObject if required.
        /// </summary>
        /// <typeparam name="T">The type for which to convert</typeparam>
        /// <param name="castObject">The object from which to convert.</param>
        /// <returns>An object of the specified type, if the conversion was successful.  Returns null otherwise.</returns>
        internal static T FromObjectAs<T>(object castObject)
        {
            T returnValue;

            // First, see if we can cast the direct type
            PSObject wrapperObject = castObject as PSObject;
            if (wrapperObject == null)
            {
                try
                {
                    returnValue = (T)castObject;
                }
                catch (InvalidCastException)
                {
                    returnValue = default;
                }
            }
            else
            {
                // Then, see if it is an PSObject wrapping the object
                try
                {
                    returnValue = (T)wrapperObject.BaseObject;
                }
                catch (InvalidCastException)
                {
                    returnValue = default;
                }
            }

            return returnValue;
        }

        [Flags]
        private enum TypeCodeTraits
        {
            None = 0x00,
            SignedInteger = 0x01,
            UnsignedInteger = 0x02,
            Floating = 0x04,
            CimIntrinsicType = 0x08,
            Decimal = 0x10,

            Integer = SignedInteger | UnsignedInteger,
            Numeric = Integer | Floating | Decimal,
        }

        private static readonly TypeCodeTraits[] s_typeCodeTraits = new TypeCodeTraits[]
        {
            /* Empty    =  0 */ TypeCodeTraits.None,
            /* Object   =  1 */ TypeCodeTraits.None,
            /* DBNull   =  2 */ TypeCodeTraits.None,
            /* Boolean  =  3 */ TypeCodeTraits.CimIntrinsicType,
            /* Char     =  4 */ TypeCodeTraits.CimIntrinsicType,
            /* SByte    =  5 */ TypeCodeTraits.SignedInteger | TypeCodeTraits.CimIntrinsicType,
            /* Byte     =  6 */ TypeCodeTraits.UnsignedInteger | TypeCodeTraits.CimIntrinsicType,
            /* Int16    =  7 */ TypeCodeTraits.SignedInteger | TypeCodeTraits.CimIntrinsicType,
            /* UInt16   =  8 */ TypeCodeTraits.UnsignedInteger | TypeCodeTraits.CimIntrinsicType,
            /* Int32    =  9 */ TypeCodeTraits.SignedInteger | TypeCodeTraits.CimIntrinsicType,
            /* UInt32   = 10 */ TypeCodeTraits.UnsignedInteger | TypeCodeTraits.CimIntrinsicType,
            /* Int64    = 11 */ TypeCodeTraits.SignedInteger | TypeCodeTraits.CimIntrinsicType,
            /* UInt64   = 12 */ TypeCodeTraits.UnsignedInteger | TypeCodeTraits.CimIntrinsicType,
            /* Single   = 13 */ TypeCodeTraits.Floating | TypeCodeTraits.CimIntrinsicType,
            /* Double   = 14 */ TypeCodeTraits.Floating | TypeCodeTraits.CimIntrinsicType,
            /* Decimal  = 15 */ TypeCodeTraits.Decimal,
            /* DateTime = 16 */ TypeCodeTraits.None | TypeCodeTraits.CimIntrinsicType,
            /*          = 17 */ TypeCodeTraits.None,
            /* String   = 18 */ TypeCodeTraits.None | TypeCodeTraits.CimIntrinsicType,
        };

        /// <summary>
        /// Verifies if type is a signed integer.
        /// </summary>
        /// <param name="typeCode">Type code to check.</param>
        /// <returns>True if type is a signed integer, false otherwise.</returns>
        internal static bool IsSignedInteger(TypeCode typeCode)
            => (s_typeCodeTraits[(int)typeCode] & TypeCodeTraits.SignedInteger) != 0;

        /// <summary>
        /// Verifies if type is an unsigned integer.
        /// </summary>
        /// <param name="typeCode">Type code to check.</param>
        /// <returns>True if type is an unsigned integer, false otherwise.</returns>
        internal static bool IsUnsignedInteger(TypeCode typeCode)
            => (s_typeCodeTraits[(int)typeCode] & TypeCodeTraits.UnsignedInteger) != 0;

        /// <summary>
        /// Verifies if type is integer.
        /// </summary>
        /// <param name="typeCode">Type code to check.</param>
        /// <returns>True if type is integer, false otherwise.</returns>
        internal static bool IsInteger(TypeCode typeCode)
            => (s_typeCodeTraits[(int)typeCode] & TypeCodeTraits.Integer) != 0;

        /// <summary>
        /// Verifies if type is a floating point number.
        /// </summary>
        /// <param name="typeCode">Type code to check.</param>
        /// <returns>True if type is floating point, false otherwise.</returns>
        internal static bool IsFloating(TypeCode typeCode)
            => (s_typeCodeTraits[(int)typeCode] & TypeCodeTraits.Floating) != 0;

        /// <summary>
        /// Verifies if type is an integer or floating point number.
        /// </summary>
        /// <param name="typeCode">Type code to check.</param>
        /// <returns>True if type is integer or floating point, false otherwise.</returns>
        internal static bool IsNumeric(TypeCode typeCode)
            => (s_typeCodeTraits[(int)typeCode] & TypeCodeTraits.Numeric) != 0;

        /// <summary>
        /// Verifies if type is a CIM intrinsic type.
        /// </summary>
        /// <param name="typeCode">Type code to check.</param>
        /// <returns>True if type is CIM intrinsic type, false otherwise.</returns>
        internal static bool IsCimIntrinsicScalarType(TypeCode typeCode)
            => (s_typeCodeTraits[(int)typeCode] & TypeCodeTraits.CimIntrinsicType) != 0;

        internal static bool IsCimIntrinsicScalarType(Type type)
        {
            Diagnostics.Assert(type != null, "Caller should verify type != null");

            // using type code we can cover all intrinsic types from the table
            // on page 11 of DSP0004, except:
            // - TimeSpan part of "datetime"
            // - <classname> ref
            TypeCode typeCode = GetTypeCode(type);
            if (IsCimIntrinsicScalarType(typeCode) && !type.IsEnum)
            {
                return true;
            }

            if (type == typeof(TimeSpan))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Verifies if type is one of the boolean types.
        /// </summary>
        /// <param name="type">Type to check.</param>
        /// <returns>True if type is one of boolean types, false otherwise.</returns>
        internal static bool IsBooleanType(Type type)
            => type == typeof(bool) || type == typeof(bool?);

        /// <summary>
        /// Verifies if type is one of switch parameter types.
        /// </summary>
        /// <param name="type">Type to check.</param>
        /// <returns>True if type is one of switch parameter types, false otherwise.</returns>
        internal static bool IsSwitchParameterType(Type type)
            => type == typeof(SwitchParameter) || type == typeof(SwitchParameter?);

        /// <summary>
        /// Verifies if type is one of boolean or switch parameter types.
        /// </summary>
        /// <param name="type">Type to check.</param>
        /// <returns>True if type if one of boolean or switch parameter types,
        /// false otherwise.</returns>
        internal static bool IsBoolOrSwitchParameterType(Type type)
            => IsBooleanType(type) || IsSwitchParameterType(type);

        /// <summary>
        /// Do the necessary conversions when using property or array assignment to a generic dictionary:
        ///
        ///     $dict.Prop = value
        ///     $dict[$Prop] = value
        ///
        /// The property typically won't need conversion, but it could.  The value is more likely in
        /// need of conversion.
        /// </summary>
        /// <param name="dictionary">The dictionary that potentially implement <see cref="IDictionary&lt;TKey,TValue&gt;"/></param>
        /// <param name="key">The object representing the key.</param>
        /// <param name="value">The value to assign.</param>
        internal static void DoConversionsForSetInGenericDictionary(IDictionary dictionary, ref object key, ref object value)
        {
            foreach (Type interfaceType in dictionary.GetType().GetInterfaces())
            {
                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                {
                    // If we get here, we know the target implements IDictionary.  We will assume
                    // that the non-generic implementation of the indexer property just forwards
                    // to the generic version, after checking the types of the key and value.
                    // This assumption holds for System.Collections.Generic.Dictionary<TKey,TValue>.

                    // If we did not make this assumption, we would be forced to generate code
                    // to call the generic indexer directly, somewhat analogous to what we do
                    // in GetEnumeratorFromIEnumeratorT.

                    Type[] genericArguments = interfaceType.GetGenericArguments();
                    key = ConvertTo(key, genericArguments[0], CultureInfo.InvariantCulture);
                    value = ConvertTo(value, genericArguments[1], CultureInfo.InvariantCulture);
                }
            }
        }

        #region type converter

        internal static PSTraceSource typeConversion = PSTraceSource.GetTracer(
            "TypeConversion",
            "Traces the type conversion algorithm",
            traceHeaders: false);
        internal static ConversionData<object> NoConversion
            = new ConversionData<object>(ConvertNoConversion, ConversionRank.None);

        private static TypeConverter GetIntegerSystemConverter(Type type)
        {
            if (type == typeof(short))
            {
                return new Int16Converter();
            }

            if (type == typeof(int))
            {
                return new Int32Converter();
            }

            if (type == typeof(long))
            {
                return new Int64Converter();
            }

            if (type == typeof(ushort))
            {
                return new UInt16Converter();
            }

            if (type == typeof(uint))
            {
                return new UInt32Converter();
            }

            if (type == typeof(ulong))
            {
                return new UInt64Converter();
            }

            if (type == typeof(byte))
            {
                return new ByteConverter();
            }

            if (type == typeof(sbyte))
            {
                return new SByteConverter();
            }

            return null;
        }

        /// backupTypeTable:
        /// Used by Remoting Rehydration Logic. While Deserializing a remote object,
        /// LocalPipeline.ExecutionContextFromTLS() might return null..In which case this
        /// TypeTable will be used to do the conversion.
        internal static object GetConverter(Type type, TypeTable backupTypeTable)
        {
            object typesXmlConverter = null;
            ExecutionContext ecFromTLS = LocalPipeline.GetExecutionContextFromTLS();
            if (ecFromTLS != null)
            {
                s_tracer.WriteLine("ecFromTLS != null");
                typesXmlConverter = ecFromTLS.TypeTable.GetTypeConverter(type.FullName);
            }

            if (typesXmlConverter == null && backupTypeTable != null)
            {
                s_tracer.WriteLine("Using provided TypeTable to get the type converter");
                typesXmlConverter = backupTypeTable.GetTypeConverter(type.FullName);
            }

            if (typesXmlConverter != null)
            {
                s_tracer.WriteLine("typesXmlConverter != null");
                return typesXmlConverter;
            }

            var typeConverters = type.GetCustomAttributes(typeof(TypeConverterAttribute), false);
            foreach (var typeConverter in typeConverters)
            {
                var attr = (TypeConverterAttribute)typeConverter;
                string assemblyQualifiedtypeName = attr.ConverterTypeName;
                typeConversion.WriteLine("{0}'s TypeConverterAttribute points to {1}.", type, assemblyQualifiedtypeName);

                // The return statement makes sure we only process the first TypeConverterAttribute
                return NewConverterInstance(assemblyQualifiedtypeName);
            }

            return null;
        }

        private static object NewConverterInstance(string assemblyQualifiedTypeName)
        {
            int typeSeparator = assemblyQualifiedTypeName.IndexOf(',');
            if (typeSeparator == -1)
            {
                typeConversion.WriteLine("Type name \"{0}\" should be assembly qualified.", assemblyQualifiedTypeName);
                return null;
            }

            string assemblyName = assemblyQualifiedTypeName.Substring(typeSeparator + 2);
            string typeName = assemblyQualifiedTypeName.Substring(0, typeSeparator);

            foreach (Assembly assembly in ClrFacade.GetAssemblies(typeName))
            {
                if (assembly.FullName == assemblyName)
                {
                    Type converterType = null;
                    try
                    {
                        converterType = assembly.GetType(typeName, false, false);
                    }
                    catch (ArgumentException e)
                    {
                        typeConversion.WriteLine("Assembly \"{0}\" threw an exception when retrieving the type \"{1}\": \"{2}\".", assemblyName, typeName, e.Message);
                        return null;
                    }

                    try
                    {
                        return Activator.CreateInstance(converterType);
                    }
                    catch (Exception e)
                    {
                        TargetInvocationException inner = e as TargetInvocationException;
                        string message = (inner == null) || (inner.InnerException == null)
                            ? e.Message
                            : inner.InnerException.Message;

                        typeConversion.WriteLine("Creating an instance of type \"{0}\" caused an exception to be thrown: \"{1}\"", assemblyQualifiedTypeName, message);
                        return null;
                    }
                }
            }

            typeConversion.WriteLine("Could not create an instance of type \"{0}\".", assemblyQualifiedTypeName);
            return null;
        }

        /// <summary>
        /// BUGBUG - brucepay Mar. 2013 - I don't think this is general enough for dynamic keywords to support arbitrary target
        /// languages with arbitrary type representations so we may need an extension point here...
        ///
        /// Maps a .NET or CIM type name string (e.g. SInt32) to the form expected by PowerShell users, namely "[typename]"
        /// If there is no mapping, then it returns null.
        /// If the string to convert is null or empty then the function returns "[object]" as the default typeless type.
        /// </summary>
        /// <param name="typeName">The typename string to convert.</param>
        /// <returns>The equivalent PowerShell representation of that type.</returns>
        public static string ConvertTypeNameToPSTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return "[object]";
            }

            if (s_nameMap.TryGetValue(typeName, out string mappedType))
            {
                return ('[' + mappedType + ']');
            }

            // Then check dot net types
            if (TypeResolver.TryResolveType(typeName, out Type dotNetType))
            {
                // Pass through the canonicalize type name we get back from the type converter...
                return '[' + ConvertTo<string>(dotNetType) + ']';
            }

            // No mapping is found, return null
            return null;
        }

        //
        // CIM name string to .NET namestring mapping table
        // (Considered using the MI routines but they didn't do quite the right thing.)
        //
        private static Dictionary<string, string> s_nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "SInt8",          "SByte" },
            { "UInt8",          "Byte" },
            { "SInt16",         "Int16" },
            { "UInt16",         "UInt16" },
            { "SInt32",         "Int32" },
            { "UInt32",         "UInt32" },
            { "SInt64",         "Int64" },
            { "UInt64",         "UInt64" },
            { "Real32",         "Single" },
            { "Real64",         "double" },
            { "Boolean",        "bool" },
            { "String",         "string" },
            { "DateTime",       "DateTime" },
            { "Reference",      "CimInstance" },
            { "Char16",         "char" },
            { "Instance",       "CimInstance" },
            { "BooleanArray",   "bool[]" },
            { "UInt8Array",     "byte[]" },
            { "SInt8Array",     "Sbyte[]" },
            { "UInt16Array",    "uint16[]" },
            { "SInt16Array",    "int64[]" },
            { "UInt32Array",    "UInt32[]" },
            { "SInt32Array",    "Int32[]" },
            { "UInt64Array",    "UInt64[]" },
            { "SInt64Array",    "Int64[]" },
            { "Real32Array",    "Single[]" },
            { "Real64Array",    "double[]" },
            { "Char16Array",    "char[]" },
            { "DateTimeArray",  "DateTime[]" },
            { "StringArray",    "string[]" },
            { "ReferenceArray", "CimInstance[]" },
            { "InstanceArray",  "CimInstance[]" },
            { "Unknown",        "UnknownType" },
        };

        #region public type conversion

        /// <summary>
        /// Converts valueToConvert to resultType.
        /// </summary>
        /// <remarks>
        /// A null valueToConvert can be converted to :
        ///     string          -   returns ""
        ///     char            -   returns '\0'
        ///     numeric types   -   returns 0 converted to the appropriate type
        ///     boolean         -   returns LanguagePrimitives.IsTrue(null)
        ///     PSObject       -   returns new PSObject())
        ///     array           -   returns an array with null in array[0]
        ///     non value types -   returns null
        ///
        /// The following conversions are considered language standard and cannot be customized:
        ///     - from derived to base class            -   returns valueToConvert intact
        ///     - to PSObject                          -   returns PSObject.AsPSObject(valueToConvert)
        ///     - to void                               -   returns AutomationNull.Value
        ///     - from array/IEnumerable to array       -   tries to convert array/IEnumerable elements
        ///     - from object of type X to array of X   -   returns an array with object as its only element
        ///     - to bool                               -   returns LanguagePrimitives.IsTrue(valueToConvert)
        ///     - to string                             -   returns a string representation of the object.
        ///                                                 In the particular case of a number to string,
        ///                                                 the conversion is culture invariant.
        ///     - from IDictionary to Hashtable         -   uses the Hashtable constructor
        ///     - to XmlDocument                        -   creates a new XmlDocument with the
        ///                                                 string representation of valueToConvert
        ///     - from string to char[]                 -   returns ((string)valueToConvert).ToCharArray()
        ///     - from string to RegEx                  -   creates a new RegEx with the string
        ///     - from string to Type                   -   looks up the type in the minishell's assemblies
        ///     - from empty string to numeric          -   returns 0 converted to the appropriate type
        ///     - from string to numeric                -   returns a culture invariant conversion
        ///     - from ScriptBlock to Delegate          -   returns a delegate wrapping that scriptblock.
        ///     - from Integer to Enumeration           -   Uses Enum.ToObject
        ///     - to WMI                                -   Instantiate a WMI instance using
        ///                                                 System.Management.ManagementObject
        ///     - to WMISearcher                        -   returns objects from running WQL query with the
        ///                                                 string representation of valueToConvert. The
        ///                                                 query is run using ManagementObjectSearcher Class.
        ///     - to WMIClass                           -   returns ManagementClass represented by the
        ///                                                 string representation of valueToConvert.
        ///     - to ADSI                               -   returns DirectoryEntry represented by the
        ///                                                 string representation of valueToConvert.
        ///     - to ADSISearcher                       -   return DirectorySearcher represented by the
        ///                                                 string representation of valueToConvert.
        ///
        /// If none of the cases above is true, the following is considered in order:
        ///
        ///    1) TypeConverter and PSTypeConverter
        ///    2) the Parse methods if the valueToConvert is a string
        ///    3) Constructors in resultType that take one parameter with type valueToConvert.GetType()
        ///    4) Implicit and explicit cast operators
        ///    5) IConvertible
        ///
        ///  If any operation above throws an exception, this exception will be wrapped into a
        ///  PSInvalidCastException and thrown resulting in no further conversion attempt.
        /// </remarks>
        /// <param name="valueToConvert">Value to be converted and returned.</param>
        /// <param name="resultType">Type to convert valueToConvert.</param>
        /// <returns>Converted value.</returns>
        /// <exception cref="ArgumentNullException">If resultType is null.</exception>
        /// <exception cref="PSInvalidCastException">If the conversion failed.</exception>
        public static object ConvertTo(object valueToConvert, Type resultType)
            => ConvertTo(
                valueToConvert,
                resultType,
                recursion: true,
                CultureInfo.InvariantCulture,
                backupTypeTable: null);

        /// <summary>
        /// Converts valueToConvert to resultType possibly considering formatProvider.
        /// </summary>
        /// <remarks>
        /// A null valueToConvert can be converted to :
        ///     string          -   returns ""
        ///     char            -   returns '\0'
        ///     numeric types   -   returns 0 converted to the appropriate type
        ///     boolean         -   returns LanguagePrimitives.IsTrue(null)
        ///     PSObject       -   returns new PSObject())
        ///     array           -   returns an array with null in array[0]
        ///     non value types -   returns null
        ///
        /// The following conversions are considered language standard and cannot be customized:
        ///     - from derived to base class            -   returns valueToConvert intact
        ///     - to PSObject                          -   returns PSObject.AsPSObject(valueToConvert)
        ///     - to void                               -   returns AutomationNull.Value
        ///     - from array/IEnumerable to array       -   tries to convert array/IEnumerable elements
        ///     - from object of type X to array of X   -   returns an array with object as its only element
        ///     - to bool                               -   returns LanguagePrimitives.IsTrue(valueToConvert)
        ///     - to string                             -   returns a string representation of the object.
        ///                                                 In the particular case of a number to string,
        ///                                                 the conversion is culture invariant.
        ///     - from IDictionary to Hashtable         -   uses the Hashtable constructor
        ///     - to XmlDocument                        -   creates a new XmlDocument with the
        ///                                                 string representation of valueToConvert
        ///     - from string to char[]                 -   returns ((string)valueToConvert).ToCharArray()
        ///     - from string to RegEx                  -   creates a new RegEx with the string
        ///     - from string to Type                   -   looks up the type in the minishell's assemblies
        ///     - from empty string to numeric          -   returns 0 converted to the appropriate type
        ///     - from string to numeric                -   returns a culture invariant conversion
        ///     - from ScriptBlock to Delegate          -   returns a delegate wrapping that scriptblock.
        ///     - from Integer to Enumeration           -   Uses Enum.ToObject
        ///
        /// If none of the cases above is true, the following is considered in order:
        ///
        ///    1) TypeConverter and PSTypeConverter
        ///    2) the Parse methods if the valueToConvert is a string
        ///    3) Constructors in resultType that take one parameter with type valueToConvert.GetType()
        ///    4) Implicit and explicit cast operators
        ///    5) IConvertible
        ///
        ///  If any operation above throws an exception, this exception will be wrapped into a
        ///  PSInvalidCastException and thrown resulting in no further conversion attempt.
        /// </remarks>
        /// <param name="valueToConvert">Value to be converted and returned.</param>
        /// <param name="resultType">Type to convert valueToConvert.</param>
        /// <param name="formatProvider">To be used in custom type conversions, to call parse and to call Convert.ChangeType.</param>
        /// <returns>Converted value.</returns>
        /// <exception cref="ArgumentNullException">If resultType is null.</exception>
        /// <exception cref="PSInvalidCastException">If the conversion failed.</exception>
        public static object ConvertTo(object valueToConvert, Type resultType, IFormatProvider formatProvider)
            => ConvertTo(valueToConvert, resultType, recursion: true, formatProvider, backupTypeTable: null);

        /// <summary>
        /// Converts PSObject to resultType.
        /// </summary>
        /// <param name="valueToConvert">Value to be converted and returned.</param>
        /// <param name="resultType">Type to convert psobject.</param>
        /// <param name="recursion">Indicates if inner properties have to be recursively converted.</param>
        /// <param name="formatProvider">To be used in custom type conversions, to call parse and to call Convert.ChangeType.</param>
        /// <param name="ignoreUnknownMembers">Indicates if Unknown members in the psobject have to be ignored if the corresponding members in resultType do not exist.</param>
        /// <returns>Converted value.</returns>
        public static object ConvertPSObjectToType(
            PSObject valueToConvert,
            Type resultType,
            bool recursion,
            IFormatProvider formatProvider,
            bool ignoreUnknownMembers)
        {
            if (valueToConvert == null)
            {
                return null;
            }

            ConstructorInfo toConstructor = resultType.GetConstructor(Type.EmptyTypes);
            var noArgumentConstructorConverter = new ConvertViaNoArgumentConstructor(toConstructor, resultType);

            return noArgumentConstructorConverter.Convert(
                PSObject.Base(valueToConvert),
                resultType,
                recursion,
                (PSObject)valueToConvert,
                formatProvider,
                backupTable: null,
                ignoreUnknownMembers);
        }

        /// <summary>
        /// Generic convertto that simplifies working with workflow.
        /// </summary>
        /// <typeparam name="T">The type of object to return</typeparam>
        /// <param name="valueToConvert"></param>
        /// <returns></returns>
        public static T ConvertTo<T>(object valueToConvert)
        {
            if (valueToConvert is T value)
            {
                return value;
            }

            return (T)ConvertTo(
                valueToConvert,
                typeof(T),
                recursion: true,
                CultureInfo.InvariantCulture,
                backupTypeTable: null);
        }

        /// <summary>
        /// Sets result to valueToConvert converted to resultType.
        /// </summary>
        /// <remarks>
        /// This method is a variant of ConvertTo that does not throw exceptions if the conversion fails.
        /// </remarks>
        /// <param name="valueToConvert">Value to be converted and returned.</param>
        /// <param name="result">Result of the conversion. This is valid only if the return is true.</param>
        /// <returns>False for conversion failure, true for success.</returns>
        public static bool TryConvertTo<T>(object valueToConvert, out T result)
        {
            if (valueToConvert is T value)
            {
                result = value;
                return true;
            }

            return TryConvertTo<T>(valueToConvert, CultureInfo.InvariantCulture, out result);
        }
        /// <summary>
        /// Sets result to valueToConvert converted to resultType considering formatProvider
        /// for custom conversions, calling the Parse method and calling Convert.ChangeType.
        /// </summary>
        /// <remarks>
        /// This method is a variant of ConvertTo that does not throw exceptions if the conversion fails.
        /// </remarks>
        /// <param name="valueToConvert">Value to be converted and returned.</param>
        /// <param name="formatProvider">Governing conversion of types.</param>
        /// <param name="result">Result of the conversion. This is valid only if the return is true.</param>
        /// <returns>False for conversion failure, true for success.</returns>
        public static bool TryConvertTo<T>(object valueToConvert, IFormatProvider formatProvider, out T result)
        {
            result = default;

            if (TryConvertTo(valueToConvert, typeof(T), formatProvider, out object res))
            {
                result = (T)res;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sets result to valueToConvert converted to resultType.
        /// </summary>
        /// <remarks>
        /// This method is a variant of ConvertTo that does not throw exceptions if the conversion fails.
        /// </remarks>
        /// <param name="valueToConvert">Value to be converted and returned.</param>
        /// <param name="resultType">Type to convert valueToConvert.</param>
        /// <param name="result">Result of the conversion. This is valid only if the return is true.</param>
        /// <returns>False for conversion failure, true for success.</returns>
        public static bool TryConvertTo(object valueToConvert, Type resultType, out object result)
            => TryConvertTo(valueToConvert, resultType, CultureInfo.InvariantCulture, out result);

        /// <summary>
        /// Sets result to valueToConvert converted to resultType considering formatProvider
        /// for custom conversions, calling the Parse method and calling Convert.ChangeType.
        /// </summary>
        /// <remarks>
        /// This method is a variant of ConvertTo that does not throw exceptions if the conversion fails.
        /// </remarks>
        /// <param name="valueToConvert">Value to be converted and returned.</param>
        /// <param name="resultType">Type to convert valueToConvert.</param>
        /// <param name="formatProvider">Governing conversion of types.</param>
        /// <param name="result">Result of the conversion. This is valid only if the return is true.</param>
        /// <returns>False for conversion failure, true for success.</returns>
        public static bool TryConvertTo(
            object valueToConvert,
            Type resultType,
            IFormatProvider formatProvider,
            out object result)
        {
            result = null;
            try
            {
                using (typeConversion.TraceScope("Converting \"{0}\" to \"{1}\".", valueToConvert, resultType))
                {
                    if (resultType == null)
                    {
                        return false;
                    }

                    var conversion = FigureConversion(valueToConvert, resultType, out bool debase);
                    if (conversion.Rank == ConversionRank.None)
                    {
                        return false;
                    }

                    result = conversion.Invoke(
                        debase ? PSObject.Base(valueToConvert) : valueToConvert,
                        resultType,
                        recurse: true,
                        debase ? (PSObject)valueToConvert : null,
                        formatProvider,
                        backupTable: null);

                    return true;
                }
            }
            catch (InvalidCastException)
            {
                return false;
            }
        }

        #endregion public type conversion

        internal class EnumMultipleTypeConverter : EnumSingleTypeConverter
        {
            public override object ConvertFrom(
                object sourceValue,
                Type destinationType,
                IFormatProvider formatProvider,
                bool ignoreCase)
                    => BaseConvertFrom(
                        sourceValue,
                        destinationType,
                        formatProvider,
                        ignoreCase,
                        multipleValues: true);
        }

        internal class EnumSingleTypeConverter : PSTypeConverter
        {
            private class EnumHashEntry
            {
                internal EnumHashEntry(
                    string[] names,
                    Array values,
                    ulong allValues,
                    bool hasNegativeValue,
                    bool hasFlagsAttribute)
                {
                    this.names = names;
                    this.values = values;
                    this.allValues = allValues;
                    this.hasNegativeValue = hasNegativeValue;
                    this.hasFlagsAttribute = hasFlagsAttribute;
                }

                internal string[] names;
                internal Array values;
                internal ulong allValues;
                internal bool hasNegativeValue;
                internal bool hasFlagsAttribute;
            }

            // This static is thread safe based on the lock in GetEnumHashEntry
            // It can be shared by Runspaces in different MiniShells
            private static readonly Dictionary<Type, EnumHashEntry> s_enumTable = new Dictionary<Type, EnumHashEntry>();
            private const int MaxEnumTableSize = 100;

            private static EnumHashEntry GetEnumHashEntry(Type enumType)
            {
                lock (s_enumTable)
                {
                    if (s_enumTable.TryGetValue(enumType, out EnumHashEntry returnValue))
                    {
                        return returnValue;
                    }

                    if (s_enumTable.Count == MaxEnumTableSize)
                    {
                        s_enumTable.Clear();
                    }

                    ulong allValues = 0;
                    bool hasNegativeValue = false;
                    Array values = Enum.GetValues(enumType);

                    // Type.GetTypeCode will return the integer type code for enumType
                    if (IsSignedInteger(enumType.GetTypeCode()))
                    {
                        foreach (object item in values)
                        {
                            var valueInt64 = Convert.ToInt64(item, CultureInfo.CurrentCulture);
                            // A negative value cannot be flag
                            if (valueInt64 < 0)
                            {
                                hasNegativeValue = true;
                                break;
                            }

                            // we know the value is not negative, so this conversion will always succeed
                            allValues |= Convert.ToUInt64(item, CultureInfo.CurrentCulture);
                        }
                    }
                    else
                    {
                        foreach (object item in values)
                        {
                            allValues |= Convert.ToUInt64(item, CultureInfo.CurrentCulture);
                        }
                    }

                    // See if the [Flag] attribute is set on this type...
                    // MemberInfo.GetCustomAttributes returns IEnumerable<Attribute> in CoreCLR.
                    bool hasFlagsAttribute = enumType.GetCustomAttributes(typeof(FlagsAttribute), false).Any();

                    returnValue = new EnumHashEntry(
                        Enum.GetNames(enumType),
                        values,
                        allValues,
                        hasNegativeValue,
                        hasFlagsAttribute);
                    s_enumTable.Add(enumType, returnValue);

                    return returnValue;
                }
            }

            public override bool CanConvertFrom(object sourceValue, Type destinationType)
                => sourceValue is string && destinationType.IsEnum;

            /// <summary>
            /// Checks if the enumValue is defined or not in enumType.
            /// </summary>
            /// <param name="enumType">Some enumeration.</param>
            /// <param name="enumValue">Supposed to be an integer.</param>
            /// <returns>
            /// </returns>
            private static bool IsDefinedEnum(object enumValue, Type enumType)
            {
                bool isDefined;
                do
                {
                    if (enumValue == null)
                    {
                        isDefined = false;
                        break;
                    }

                    EnumHashEntry enumHashEntry = GetEnumHashEntry(enumType);

                    // An enumeration with a negative value should not be treated as flags
                    // so IsValueFlagDefined cannot determine the result, and as far as it knows,
                    // it is defined.
                    if (enumHashEntry.hasNegativeValue)
                    {
                        isDefined = true;
                        break;
                    }

                    // Type.GetTypeCode will return the integer type code for enumValue.GetType()
                    if (IsSignedInteger(enumValue.GetType().GetTypeCode()))
                    {
                        var enumValueInt64 = Convert.ToInt64(enumValue, CultureInfo.CurrentCulture);

                        // A negative value cannot be flag, so we return false
                        if (enumValueInt64 < 0)
                        {
                            isDefined = false;
                            break;
                        }
                    }

                    // the if above, guarantees that even if it is an Int64 it is > 0
                    // so the conversion should always work.
                    var enumValueUInt64 = Convert.ToUInt64(enumValue, CultureInfo.CurrentCulture);

                    if (enumHashEntry.hasFlagsAttribute)
                    {
                        // This expression will result in a "1 bit" for bits that are
                        // set in enumValueInt64 but not set in enumHashEntry.allValues,
                        // and a "0 bit" otherwise. Any "bit 1" in the result, indicates this is not defined.
                        isDefined = ((enumValueUInt64 | enumHashEntry.allValues) ^ enumHashEntry.allValues) == 0;
                    }
                    else
                    {
                        // If flags is not set, then see if this value is in the list
                        // of valid values.
                        isDefined = Array.IndexOf(enumHashEntry.values, enumValue) >= 0;
                    }
                } while (false);

                return isDefined;
            }

            /// <summary>
            /// Throws if the enumType enumeration has no negative values, but the enumValue is not
            /// defined in enumType.
            /// </summary>
            /// <param name="enumType">Some enumeration.</param>
            /// <param name="enumValue">Supposed to be an integer.</param>
            /// <param name="errorId">The error id to be used when throwing an exception.</param>
            internal static void ThrowForUndefinedEnum(string errorId, object enumValue, Type enumType)
                => ThrowForUndefinedEnum(errorId, enumValue, enumValue, enumType);

            /// <summary>
            /// Throws if the enumType enumeration has no negative values, but the enumValue is not
            /// defined in enumType.
            /// </summary>
            /// <param name="errorId">The error id to be used when throwing an exception.</param>
            /// <param name="enumValue">Value to validate.</param>
            /// <param name="valueToUseToThrow">Value to use while throwing an exception.</param>
            /// <param name="enumType">The enum type to validate the enumValue with.</param>
            /// <remarks>
            /// <paramref name="valueToUseToThrow"/> is used by those callers who want the exception
            /// to contain a different value than the one that is validated.
            /// This will enable callers to take different forms of input -> convert to enum using
            /// Enum.Object -> then validate using this method.
            /// </remarks>
            internal static void ThrowForUndefinedEnum(
                string errorId,
                object enumValue,
                object valueToUseToThrow,
                Type enumType)
            {
                if (!IsDefinedEnum(enumValue, enumType))
                {
                    typeConversion.WriteLine("Value {0} is not defined in the Enum {1}.", valueToUseToThrow, enumType);

                    throw new PSInvalidCastException(
                        errorId,
                        innerException: null,
                        ExtendedTypeSystem.InvalidCastExceptionEnumerationNoValue,
                        valueToUseToThrow,
                        enumType,
                        EnumValues(enumType));
                }
            }

            internal static string EnumValues(Type enumType)
                => string.Join(
                    CultureInfo.CurrentUICulture.TextInfo.ListSeparator,
                    GetEnumHashEntry(enumType).names);

            public override object ConvertFrom(
                object sourceValue,
                Type destinationType,
                IFormatProvider formatProvider,
                bool ignoreCase)
                    => BaseConvertFrom(sourceValue, destinationType, formatProvider, ignoreCase, multipleValues: false);

            protected static object BaseConvertFrom(
                object sourceValue,
                Type destinationType,
                IFormatProvider formatProvider,
                bool ignoreCase,
                bool multipleValues)
            {
                Diagnostics.Assert(sourceValue != null, "the type converter has a special case for null source values");

                string sourceValueString = sourceValue as string;
                if (sourceValueString == null)
                {
                    throw new PSInvalidCastException(
                        "InvalidCastEnumFromTypeNotAString",
                        innerException: null,
                        ExtendedTypeSystem.InvalidCastException,
                        sourceValue,
                        ObjectToTypeNameString(sourceValue),
                        destinationType);
                }

                Diagnostics.Assert(destinationType.IsEnum, "EnumSingleTypeConverter is only applied to enumerations");

                if (sourceValueString.Length == 0)
                {
                    throw new PSInvalidCastException(
                        "InvalidCastEnumFromEmptyString",
                        innerException: null,
                        ExtendedTypeSystem.InvalidCastException,
                        sourceValue,
                        ObjectToTypeNameString(sourceValue),
                        destinationType);
                }

                sourceValueString = sourceValueString.Trim();
                if (sourceValueString.Length == 0)
                {
                    throw new PSInvalidCastException(
                        "InvalidEnumCastFromEmptyStringAfterTrim",
                        innerException: null,
                        ExtendedTypeSystem.InvalidCastException,
                        sourceValue,
                        ObjectToTypeNameString(sourceValue),
                        destinationType);
                }

                if (char.IsDigit(sourceValueString[0])
                    || sourceValueString[0] == '+'
                    || sourceValueString[0] == '-')
                {
                    Type underlyingType = Enum.GetUnderlyingType(destinationType);
                    try
                    {
                        object result = Enum.ToObject(
                            destinationType,
                            Convert.ChangeType(sourceValueString, underlyingType, formatProvider));
                        ThrowForUndefinedEnum(
                            "UndefinedInEnumSingleTypeConverter",
                            result,
                            sourceValueString,
                            destinationType);

                        return result;
                    }
                    // Enum.ToObject and Convert.ChangeType might throw unadvertised exceptions
                    catch (Exception)
                    {
                        // we still want to try non numeric match
                    }
                }

                string[] sourceValueEntries;
                WildcardPattern[] fromValuePatterns;
                if (!multipleValues)
                {
                    if (sourceValueString.Contains(","))
                    {
                        throw new PSInvalidCastException(
                            "InvalidCastEnumCommaAndNoFlags",
                            innerException: null,
                            ExtendedTypeSystem.InvalidCastExceptionEnumerationNoFlagAndComma,
                            sourceValue,
                            destinationType);
                    }

                    sourceValueEntries = new string[] { sourceValueString };
                    fromValuePatterns = new WildcardPattern[1];
                    if (WildcardPattern.ContainsWildcardCharacters(sourceValueString))
                    {
                        fromValuePatterns[0] = WildcardPattern.Get(
                            sourceValueString,
                            ignoreCase ? WildcardOptions.IgnoreCase : WildcardOptions.None);
                    }
                    else
                    {
                        fromValuePatterns[0] = null;
                    }
                }
                else
                {
                    sourceValueEntries = sourceValueString.Split(Utils.Separators.Comma);
                    fromValuePatterns = new WildcardPattern[sourceValueEntries.Length];
                    for (int i = 0; i < sourceValueEntries.Length; i++)
                    {
                        string sourceValueEntry = sourceValueEntries[i];
                        if (WildcardPattern.ContainsWildcardCharacters(sourceValueEntry))
                        {
                            fromValuePatterns[i] = WildcardPattern.Get(
                                sourceValueEntry,
                                ignoreCase ? WildcardOptions.IgnoreCase : WildcardOptions.None);
                        }
                        else
                        {
                            fromValuePatterns[i] = null;
                        }
                    }
                }

                EnumHashEntry enumHashEntry = GetEnumHashEntry(destinationType);
                string[] names = enumHashEntry.names;
                Array values = enumHashEntry.values;
                ulong returnUInt64 = 0;
                StringComparison ignoreCaseOpt = ignoreCase
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                for (int i = 0; i < sourceValueEntries.Length; i++)
                {
                    string sourceValueEntry = sourceValueEntries[i];
                    WildcardPattern fromValuePattern = fromValuePatterns[i];

                    bool foundOne = false;
                    for (int j = 0; j < names.Length; j++)
                    {
                        string name = names[j];

                        if (fromValuePattern != null)
                        {
                            if (!fromValuePattern.IsMatch(name))
                            {
                                continue;
                            }
                        }
                        else if (string.Compare(sourceValueEntry, name, ignoreCaseOpt) != 0)
                        {
                            continue;
                        }

                        if (!multipleValues && foundOne)
                        {
                            object firstValue = Enum.ToObject(destinationType, returnUInt64);
                            object secondValue = Enum.ToObject(
                                destinationType,
                                Convert.ToUInt64(values.GetValue(i), CultureInfo.CurrentCulture));

                            throw new PSInvalidCastException(
                                "InvalidCastEnumTwoStringsFoundAndNoFlags",
                                innerException: null,
                                ExtendedTypeSystem.InvalidCastExceptionEnumerationMoreThanOneValue,
                                sourceValue,
                                destinationType,
                                firstValue,
                                secondValue);
                        }

                        foundOne = true;
                        returnUInt64 |= Convert.ToUInt64(values.GetValue(j), CultureInfo.CurrentCulture);
                    }

                    if (!foundOne)
                    {
                        throw new PSInvalidCastException(
                            "InvalidCastEnumStringNotFound",
                            innerException: null,
                            ExtendedTypeSystem.InvalidCastExceptionEnumerationNoValue,
                            sourceValueEntry,
                            destinationType,
                            EnumValues(destinationType));
                    }
                }

                return Enum.ToObject(destinationType, returnUInt64);
            }

            public override bool CanConvertTo(object sourceValue, Type destinationType) => false;

            public override object ConvertTo(
                object sourceValue,
                Type destinationType,
                IFormatProvider formatProvider,
                bool ignoreCase)
                    => throw PSTraceSource.NewNotSupportedException();
        }

        /// <summary>
        /// There might be many cast operators in a Type A that take Type A. Each operator will have a
        /// different return type. Because of that we cannot call GetMethod since it would cause a
        /// AmbiguousMatchException. This auxiliary method calls GetMember to find the right method.
        /// </summary>
        /// <param name="methodName">Either op_Explicit or op_Implicit, at the moment.</param>
        /// <param name="targetType">The type to look for an operator.</param>
        /// <param name="originalType">Type of the only parameter the operator method should have.</param>
        /// <param name="resultType">Return type of the operator method.</param>
        /// <returns>A cast operator method, or null if not found.</returns>
        private static MethodInfo FindCastOperator(
            string methodName,
            Type targetType,
            Type originalType,
            Type resultType)
        {
            using (typeConversion.TraceScope("Looking for \"{0}\" cast operator.", methodName))
            {
                // Get multiple matched Public & Static methods
                const BindingFlags flagsToUse = BindingFlags.FlattenHierarchy
                    | BindingFlags.Public
                    | BindingFlags.Static
                    | BindingFlags.InvokeMethod;
                MemberInfo[] methods = targetType.GetMember(methodName, flagsToUse);
                foreach (MethodInfo method in methods)
                {
                    if (!resultType.IsAssignableFrom(method.ReturnType))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1 || !parameters[0].ParameterType.IsAssignableFrom(originalType))
                    {
                        continue;
                    }

                    typeConversion.WriteLine(
                        "Found \"{0}\" cast operator in type {1}.",
                        methodName,
                        targetType.FullName);
                    return method;
                }

                typeConversion.TraceScope("Cast operator for \"{0}\" not found.", methodName);
                return null;
            }
        }

        private static object ConvertNumericThroughDouble(object valueToConvert, Type resultType)
        {
            using (typeConversion.TraceScope("Numeric Conversion through double."))
            {
                // Eventual exceptions here are caught by the caller
                object intermediate = Convert.ChangeType(
                    valueToConvert,
                    typeof(double),
                    CultureInfo.InvariantCulture.NumberFormat);

                return Convert.ChangeType(
                    intermediate,
                    resultType,
                    CultureInfo.InvariantCulture.NumberFormat);
            }
        }

#if !UNIX
        private static ManagementObject ConvertToWMI(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Standard type conversion to a ManagementObject.");

            string valueToConvertString;
            try
            {
                valueToConvertString = PSObject.ToString(
                    context: null,
                    valueToConvert,
                    separator: "\n",
                    format: null,
                    formatProvider: null,
                    recurse: true,
                    unravelEnumeratorOnRecurse: true);
            }
            catch (ExtendedTypeSystemException e)
            {
                typeConversion.WriteLine("Exception converting value to string: {0}", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastGetStringToWMI",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionNoStringForConversion,
                    resultType.ToString(),
                    e.Message);
            }

            try
            {
                ManagementObject wmiObject = new ManagementObject(valueToConvertString);

                // ManagementObject will not throw if path does not contain valid key
                if (wmiObject.SystemProperties["__CLASS"] == null)
                {
                    string message = StringUtil.Format(ExtendedTypeSystem.InvalidWMIPath, valueToConvertString);
                    throw new PSInvalidCastException(message);
                }

                return wmiObject;
            }
            catch (Exception wmiObjectException)
            {
                typeConversion.WriteLine("Exception creating WMI object: \"{0}\".", wmiObjectException.Message);

                throw new PSInvalidCastException(
                    "InvalidCastToWMI",
                    wmiObjectException,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    wmiObjectException.Message);
            }
        }

        private static ManagementObjectSearcher ConvertToWMISearcher(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Standard type conversion to a collection of ManagementObjects.");

            string valueToConvertString;
            try
            {
                valueToConvertString = PSObject.ToString(
                    context: null,
                    valueToConvert,
                    separator: "\n",
                    format: null,
                    formatProvider: null,
                    recurse: true,
                    unravelEnumeratorOnRecurse: true);
            }
            catch (ExtendedTypeSystemException e)
            {
                typeConversion.WriteLine("Exception converting value to string: {0}", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastGetStringToWMISearcher",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionNoStringForConversion,
                    resultType.ToString(),
                    e.Message);
            }

            try
            {
                ManagementObjectSearcher objectSearcher = new ManagementObjectSearcher(valueToConvertString);
                return objectSearcher;
            }
            catch (Exception objectSearcherException)
            {
                typeConversion.WriteLine("Exception running WMI object query: \"{0}\".", objectSearcherException.Message);

                throw new PSInvalidCastException(
                    "InvalidCastToWMISearcher",
                    objectSearcherException,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    objectSearcherException.Message);
            }
        }

        private static ManagementClass ConvertToWMIClass(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Standard type conversion to a ManagementClass.");

            string valueToConvertString;
            try
            {
                valueToConvertString = PSObject.ToString(
                    context: null,
                    valueToConvert,
                    separator: "\n",
                    format: null,
                    formatProvider: null,
                    recurse: true,
                    unravelEnumeratorOnRecurse: true);
            }
            catch (ExtendedTypeSystemException e)
            {
                typeConversion.WriteLine("Exception converting value to string: {0}", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastGetStringToWMIClass",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionNoStringForConversion,
                    resultType.ToString(),
                    e.Message);
            }

            try
            {
                ManagementClass wmiClass = new System.Management.ManagementClass(valueToConvertString);

                // ManagementClass will not throw if the path specified is not
                // a valid class.
                if (wmiClass.SystemProperties["__CLASS"] == null)
                {
                    string message = StringUtil.Format(ExtendedTypeSystem.InvalidWMIClassPath, valueToConvertString);
                    throw new PSInvalidCastException(message);
                }

                return wmiClass;
            }
            catch (Exception wmiClassException)
            {
                typeConversion.WriteLine("Exception creating WMI class: \"{0}\".", wmiClassException.Message);

                throw new PSInvalidCastException(
                    "InvalidCastToWMIClass",
                    wmiClassException,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    wmiClassException.Message);
            }
        }

        private static DirectoryEntry ConvertToADSI(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Standard type conversion to DirectoryEntry.");

            string valueToConvertString;
            try
            {
                valueToConvertString = PSObject.ToString(
                    context: null,
                    valueToConvert,
                    separator: "\n",
                    format: null,
                    formatProvider: null,
                    recurse: true,
                    unravelEnumeratorOnRecurse: true);
            }
            catch (ExtendedTypeSystemException e)
            {
                typeConversion.WriteLine("Exception converting value to string: {0}", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastGetStringToADSIClass",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionNoStringForConversion,
                    resultType.ToString(),
                    e.Message);
            }

            try
            {
                DirectoryEntry entry = new DirectoryEntry(valueToConvertString);
                return entry;
            }
            catch (Exception adsiClassException)
            {
                typeConversion.WriteLine("Exception creating ADSI class: \"{0}\".", adsiClassException.Message);

                throw new PSInvalidCastException(
                    "InvalidCastToADSIClass",
                    adsiClassException,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    adsiClassException.Message);
            }
        }

        private static DirectorySearcher ConvertToADSISearcher(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Standard type conversion to ADSISearcher");

            try
            {
                return new DirectorySearcher((string)valueToConvert);
            }
            catch (Exception e)
            {
                typeConversion.WriteLine("Exception creating ADSI searcher: \"{0}\".", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastToADSISearcher",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    e.Message);
            }
        }
#endif

        private static StringCollection ConvertToStringCollection(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Standard type conversion to a StringCollection.");

            var stringCollection = new StringCollection();
            AddItemsToCollection(valueToConvert, resultType, formatProvider, backupTable, stringCollection);

            return stringCollection;
        }

        private static void AddItemsToCollection(
            object valueToConvert,
            Type resultType,
            IFormatProvider formatProvider,
            TypeTable backupTable,
            StringCollection stringCollection)
        {
            try
            {
                var stringArrayValue = (string[])ConvertTo(
                    valueToConvert,
                    typeof(string[]),
                    recursion: false,
                    formatProvider,
                    backupTable);
                stringCollection.AddRange(stringArrayValue);
            }
            catch (PSInvalidCastException)
            {
                typeConversion.WriteLine("valueToConvert contains non-string type values");

                var innerException = new ArgumentException(StringUtil.Format(
                    ExtendedTypeSystem.CannotConvertValueToStringArray,
                    valueToConvert.ToString()));

                throw new PSInvalidCastException(
                    StringUtil.Format("InvalidCastTo{0}Class", resultType.Name),
                    innerException,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    innerException.Message);
            }
            catch (Exception ex)
            {
                typeConversion.WriteLine("Exception creating StringCollection class: \"{0}\".", ex.Message);

                throw new PSInvalidCastException(
                    StringUtil.Format("InvalidCastTo{0}Class", resultType.Name),
                    innerException: ex,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    ex.Message);
            }
        }

        private static XmlDocument ConvertToXml(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            using (typeConversion.TraceScope("Standard type conversion to XmlDocument."))
            {
                string valueToConvertString;
                try
                {
                    valueToConvertString = PSObject.ToString(
                        context: null,
                        valueToConvert,
                        separator: "\n",
                        format: null,
                        formatProvider: null,
                        recurse: true,
                        unravelEnumeratorOnRecurse: true);
                }
                catch (ExtendedTypeSystemException e)
                {
                    typeConversion.WriteLine("Exception converting value to string: {0}", e.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastGetStringToXmlDocument",
                        innerException: e,
                        ExtendedTypeSystem.InvalidCastExceptionNoStringForConversion,
                        resultType.ToString(),
                        e.Message);
                }

                try
                {
                    using TextReader textReader = new StringReader(valueToConvertString);

                    // Win8: 481571 Enforcing "PreserveWhitespace" breaks XML pretty printing
                    XmlReaderSettings settings = InternalDeserializer.XmlReaderSettingsForUntrustedXmlDocument.Clone();
                    settings.IgnoreWhitespace = true;
                    settings.IgnoreProcessingInstructions = false;
                    settings.IgnoreComments = false;

                    XmlReader xmlReader = XmlReader.Create(textReader, settings);
                    XmlDocument xmlDocument = new XmlDocument
                    {
                        PreserveWhitespace = false
                    };
                    xmlDocument.Load(xmlReader);

                    return xmlDocument;
                }
                catch (Exception loadXmlException)
                {
                    typeConversion.WriteLine("Exception loading XML: \"{0}\".", loadXmlException.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastToXmlDocument",
                        loadXmlException,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        loadXmlException.Message);
                }
            }
        }

        private static CultureInfo GetCultureFromFormatProvider(IFormatProvider formatProvider)
            => formatProvider as CultureInfo ?? CultureInfo.InvariantCulture;

        /// backupTypeTable:
        /// Used by Remoting Rehydration Logic. While Deserializing a remote object,
        /// LocalPipeline.ExecutionContextFromTLS() might return null..In which case this
        /// TypeTable will be used to do the conversion.
        private static bool IsCustomTypeConversion(
            object valueToConvert,
            Type resultType,
            IFormatProvider formatProvider,
            out object result,
            TypeTable backupTypeTable)
        {
            using (typeConversion.TraceScope("Custom type conversion."))
            {
                object baseValueToConvert = PSObject.Base(valueToConvert);
                Type originalType = baseValueToConvert.GetType();

                // first using ConvertTo for the original type
                object valueConverter = GetConverter(originalType, backupTypeTable);

                if ((valueConverter != null))
                {
                    if (valueConverter is TypeConverter valueTypeConverter)
                    {
                        typeConversion.WriteLine("Original type's converter is TypeConverter.");
                        if (valueTypeConverter.CanConvertTo(resultType))
                        {
                            typeConversion.WriteLine("TypeConverter can convert to resultType.");
                            try
                            {
                                result = valueTypeConverter.ConvertTo(
                                    context: null,
                                    GetCultureFromFormatProvider(formatProvider),
                                    baseValueToConvert,
                                    resultType);
                                return true;
                            }
                            catch (Exception e)
                            {
                                typeConversion.WriteLine(
                                    "Exception converting with Original type's TypeConverter: \"{0}\".",
                                    e.Message);

                                throw new PSInvalidCastException(
                                    "InvalidCastTypeConvertersConvertTo",
                                    innerException: e,
                                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                                    valueToConvert.ToString(),
                                    resultType.ToString(),
                                    e.Message);
                            }
                        }
                        else
                        {
                            typeConversion.WriteLine("TypeConverter cannot convert to resultType.");
                        }
                    }

                    if (valueConverter is PSTypeConverter valuePSTypeConverter)
                    {
                        typeConversion.WriteLine("Original type's converter is PSTypeConverter.");
                        PSObject psValueToConvert = PSObject.AsPSObject(valueToConvert);
                        if (valuePSTypeConverter.CanConvertTo(psValueToConvert, resultType))
                        {
                            typeConversion.WriteLine("Original type's PSTypeConverter can convert to resultType.");
                            try
                            {
                                result = valuePSTypeConverter.ConvertTo(
                                    psValueToConvert,
                                    resultType,
                                    formatProvider,
                                    ignoreCase: true);
                                return true;
                            }
                            catch (Exception e)
                            {
                                typeConversion.WriteLine(
                                    "Exception converting with Original type's PSTypeConverter: \"{0}\".",
                                    e.Message);

                                throw new PSInvalidCastException(
                                    "InvalidCastPSTypeConvertersConvertTo",
                                    innerException: e,
                                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                                    valueToConvert.ToString(),
                                    resultType.ToString(),
                                    e.Message);
                            }
                        }
                        else
                        {
                            typeConversion.WriteLine("Original type's PSTypeConverter cannot convert to resultType.");
                        }
                    }
                }

                s_tracer.WriteLine("No converter found in original type.");

                // now ConvertFrom for the destination type
                valueConverter = GetConverter(resultType, backupTypeTable);
                if (valueConverter != null)
                {
                    if (valueConverter is TypeConverter valueTypeConverter)
                    {
                        typeConversion.WriteLine(
                            "Destination type's converter is TypeConverter that can convert from originalType.");
                        if (valueTypeConverter.CanConvertFrom(originalType))
                        {
                            typeConversion.WriteLine("Destination type's converter can convert from originalType.");
                            try
                            {
                                result = valueTypeConverter.ConvertFrom(
                                    context: null,
                                    GetCultureFromFormatProvider(formatProvider),
                                    baseValueToConvert);
                                return true;
                            }
                            catch (Exception e)
                            {
                                typeConversion.WriteLine(
                                    "Exception converting with Destination type's TypeConverter: \"{0}\".",
                                    e.Message);

                                throw new PSInvalidCastException(
                                    "InvalidCastTypeConvertersConvertFrom",
                                    innerException: e,
                                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                                    valueToConvert.ToString(),
                                    resultType.ToString(),
                                    e.Message);
                            }
                        }
                        else
                        {
                            typeConversion.WriteLine("Destination type's converter cannot convert from originalType.");
                        }
                    }

                    if (valueConverter is PSTypeConverter valuePSTypeConverter)
                    {
                        typeConversion.WriteLine("Destination type's converter is PSTypeConverter.");
                        PSObject psValueToConvert = PSObject.AsPSObject(valueToConvert);
                        if (valuePSTypeConverter.CanConvertFrom(psValueToConvert, resultType))
                        {
                            typeConversion.WriteLine("Destination type's converter can convert from originalType.");
                            try
                            {
                                result = valuePSTypeConverter.ConvertFrom(
                                    psValueToConvert,
                                    resultType,
                                    formatProvider,
                                    ignoreCase: true);
                                return true;
                            }
                            catch (Exception e)
                            {
                                typeConversion.WriteLine(
                                    "Exception converting with Destination type's PSTypeConverter: \"{0}\".",
                                    e.Message);

                                throw new PSInvalidCastException(
                                    "InvalidCastPSTypeConvertersConvertFrom",
                                    innerException: e,
                                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                                    valueToConvert.ToString(),
                                    resultType.ToString(),
                                    e.Message);
                            }
                        }
                        else
                        {
                            typeConversion.WriteLine("Destination type's converter cannot convert from originalType.");
                        }
                    }
                }

                result = null;
                return false;
            }
        }

        private static object ConvertNumericChar(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            try
            {
                // Convert char through int to float/double.
                object result = Convert.ChangeType(
                    Convert.ChangeType(valueToConvert, typeof(int), formatProvider),
                    resultType,
                    formatProvider);

                typeConversion.WriteLine("Numeric conversion succeeded.");
                return result;
            }
            catch (Exception e)
            {
                typeConversion.WriteLine("Exception converting with IConvertible: \"{0}\".", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastIConvertible",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    e.Message);
            }
        }

        private static object ConvertNumeric(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            try
            {
                object result = Convert.ChangeType(valueToConvert, resultType, formatProvider);

                typeConversion.WriteLine("Numeric conversion succeeded.");
                return result;
            }
            catch (Exception e)
            {
                typeConversion.WriteLine("Exception converting with IConvertible: \"{0}\".", e.Message);
                throw new PSInvalidCastException(
                    "InvalidCastIConvertible",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    e.Message);
            }
        }

        private static char[] ConvertStringToCharArray(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            Diagnostics.Assert(valueToConvert is string, "Value to convert must be string");
            typeConversion.WriteLine("Returning value to convert's ToCharArray().");
            // This conversion is not wrapped in a try/catch because it can't raise an exception
            // unless the string object has been corrupted.
            return ((string)valueToConvert).ToCharArray();
        }

        private static Regex ConvertStringToRegex(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            Diagnostics.Assert(valueToConvert is string, "Value to convert must be string");
            typeConversion.WriteLine("Returning new RegEx(value to convert).");

            try
            {
                return new Regex((string)valueToConvert);
            }
            catch (Exception regexException)
            {
                typeConversion.WriteLine("Exception in RegEx constructor: \"{0}\".", regexException.Message);

                throw new PSInvalidCastException(
                    "InvalidCastFromStringToRegex",
                    regexException,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    regexException.Message);
            }
        }

        private static Microsoft.Management.Infrastructure.CimSession ConvertStringToCimSession(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            Diagnostics.Assert(valueToConvert is string, "Value to convert must be string");
            typeConversion.WriteLine("Returning CimSession.Create(value to convert).");

            try
            {
                return Microsoft.Management.Infrastructure.CimSession.Create((string)valueToConvert);
            }
            catch (Microsoft.Management.Infrastructure.CimException cimException)
            {
                typeConversion.WriteLine("Exception in CimSession.Create: \"{0}\".", cimException.Message);
                throw new PSInvalidCastException(
                    "InvalidCastFromStringToCimSession",
                    cimException,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(), resultType.ToString(), cimException.Message);
            }
        }

        private static Type ConvertStringToType(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            Diagnostics.Assert(valueToConvert is string, "Value to convert must be a string");

            Type namedType = TypeResolver.ResolveType((string)valueToConvert, out Exception exception);
            if (namedType == null)
            {
                if (exception is PSInvalidCastException)
                {
                    throw exception;
                }

                throw new PSInvalidCastException(
                    "InvalidCastFromStringToType",
                    exception,
                    ExtendedTypeSystem.InvalidCastException,
                    valueToConvert.ToString(),
                    ObjectToTypeNameString(valueToConvert),
                    resultType.ToString());
            }

            return namedType;
        }

        /// <summary>
        /// We need to add this built-in converter because in FullCLR, System.Uri has a TypeConverter attribute
        /// declared: [TypeConverter(typeof(UriTypeConverter))], so the conversion from 'string' to 'Uri' is
        /// actually taken care of by 'UriTypeConverter'. However, the type 'UriTypeConverter' is not available
        /// in CoreCLR, and thus the conversion from 'string' to 'Uri' would show a different behavior.
        ///
        /// Therefore, we just add this built-in string-to-uri converter using the same logic 'UriTypeConverter'
        /// is using in FullCLR, so the conversion behavior will be the same on desktop powershell and powershell core.
        /// </summary>
        private static Uri ConvertStringToUri(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            try
            {
                Diagnostics.Assert(valueToConvert is string, "Value to convert must be a string");

                return new Uri((string)valueToConvert, UriKind.RelativeOrAbsolute);
            }
            catch (Exception uriException)
            {
                typeConversion.WriteLine("Exception in Uri constructor: \"{0}\".", uriException.Message);
                throw new PSInvalidCastException(
                    "InvalidCastFromStringToUri",
                    uriException,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    uriException.Message);
            }
        }

        /// <summary>
        /// Attempts to use Parser.ScanNumber to get the value of a numeric string.
        /// </summary>
        /// <param name="strToConvert">The string to convert to a number.</param>
        /// <param name="resultType">The resulting value type to convert to.</param>
        /// <param name="result">The resulting numeric value.</param>
        /// <returns>
        /// True if the parse succeeds, false if a parse exception arises.
        /// In all other cases, an exception will be thrown.
        /// </returns>
        private static bool TryScanNumber(string strToConvert, Type resultType, out object result)
        {
            try
            {
                result = Convert.ChangeType(
                    Parser.ScanNumber(strToConvert, resultType, shouldTryCoercion: false),
                    resultType,
                    CultureInfo.InvariantCulture.NumberFormat);
                return true;
            }
            catch (Exception)
            {
                // Parse or convert failed
                result = null;
                return false;
            }
        }

        private static object ConvertStringToInteger(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            var strToConvert = valueToConvert as string;

            Diagnostics.Assert(strToConvert != null, "Value to convert must be a string");
            Diagnostics.Assert(IsNumeric(GetTypeCode(resultType)), "Result type must be numeric");

            if (strToConvert.Length == 0)
            {
                typeConversion.WriteLine("Returning numeric zero.");

                // This is not wrapped in a try/catch because it can't fail.
                return Convert.ChangeType(0, resultType, CultureInfo.InvariantCulture);
            }

            typeConversion.WriteLine("Converting to integer.");

            TypeConverter integerConverter = GetIntegerSystemConverter(resultType);
            try
            {
                if (TryScanNumber(strToConvert, resultType, out object result))
                {
                    return result;
                }
                else
                {
                    return integerConverter.ConvertFrom(strToConvert);
                }
            }
            catch (Exception e)
            {
                // This catch has one extra reason to be generic (Exception e).
                // integerConverter.ConvertFrom wraps its exceptions in a System.Exception.
                if (e.InnerException != null)
                {
                    e = e.InnerException;
                }

                typeConversion.WriteLine("Exception converting to integer: \"{0}\".", e.Message);

                if (e is FormatException)
                {
                    typeConversion.WriteLine("Converting to integer passing through double.");

                    try
                    {
                        return ConvertNumericThroughDouble(strToConvert, resultType);
                    }
                    catch (Exception ex) // swallow non-severe exceptions
                    {
                        typeConversion.WriteLine(
                            "Exception converting to integer through double: \"{0}\".",
                            ex.Message);
                    }
                }

                throw new PSInvalidCastException(
                    "InvalidCastFromStringToInteger",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    strToConvert,
                    resultType.ToString(),
                    e.Message);
            }
        }

        private static object ConvertStringToDecimal(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            Diagnostics.Assert(valueToConvert is string, "Value to convert must be a string");

            var strToConvert = valueToConvert as string;
            if (strToConvert.Length == 0)
            {
                typeConversion.WriteLine("Returning numeric zero.");

                // This is not wrapped in a try/catch because it can't fail.
                return Convert.ChangeType(0, resultType, CultureInfo.InvariantCulture);
            }

            typeConversion.WriteLine("Converting to decimal.");

            try
            {
                typeConversion.WriteLine("Parsing string value to account for multipliers and type suffixes");

                return TryScanNumber(strToConvert, resultType, out object result)
                    ? result
                    : Convert.ChangeType(strToConvert, resultType, CultureInfo.InvariantCulture.NumberFormat);
            }
            catch (Exception e)
            {
                typeConversion.WriteLine(
                    "Exception converting to decimal: \"{0}\". Converting to decimal passing through double.",
                    e.Message);

                if (e is FormatException)
                {
                    try
                    {
                        return ConvertNumericThroughDouble(strToConvert, resultType);
                    }
                    catch (Exception ex)
                    {
                        typeConversion.WriteLine(
                            "Exception converting to integer through double: \"{0}\".",
                            ex.Message);
                    }
                }

                throw new PSInvalidCastException(
                    "InvalidCastFromStringToDecimal",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    strToConvert,
                    resultType.ToString(),
                    e.Message);
            }
        }

        private static object ConvertStringToReal(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            Diagnostics.Assert(valueToConvert is string, "Value to convert must be a string");

            var strToConvert = valueToConvert as string;
            if (strToConvert.Length == 0)
            {
                typeConversion.WriteLine("Returning numeric zero.");

                // This is not wrapped in a try/catch because it can't fail.
                return System.Convert.ChangeType(0, resultType, CultureInfo.InvariantCulture);
            }

            typeConversion.WriteLine("Converting to double or single.");

            try
            {
                typeConversion.WriteLine("Parsing string value to account for multipliers and type suffixes");

                if (TryScanNumber(strToConvert, resultType, out object result))
                {
                    return result;
                }
                else
                {
                    return Convert.ChangeType(strToConvert, resultType, CultureInfo.InvariantCulture.NumberFormat);
                }
            }
            catch (Exception e)
            {
                typeConversion.WriteLine("Exception converting to double or single: \"{0}\".", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastFromStringToDoubleOrSingle",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    strToConvert,
                    resultType.ToString(),
                    e.Message);
            }
        }

        private static object ConvertAssignableFrom(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Result type is assignable from value to convert's type");
            return valueToConvert;
        }

        private static PSObject ConvertToPSObject(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Returning PSObject.AsPSObject(valueToConvert).");
            return PSObject.AsPSObject(valueToConvert);
        }

        private static object ConvertToVoid(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("returning AutomationNull.Value.");
            return AutomationNull.Value;
        }

        private static bool ConvertClassToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting ref to boolean.");
            return valueToConvert != null;
        }

        private static bool ConvertValueToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting value to boolean.");
            return true;
        }

        private static bool ConvertStringToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting string to boolean.");
            return IsTrue((string)valueToConvert);
        }

        private static bool ConvertInt16ToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((short)valueToConvert) != default(short);

        private static bool ConvertInt32ToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((int)valueToConvert) != default(int);

        private static bool ConvertInt64ToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((long)valueToConvert) != default(long);

        private static bool ConvertUInt16ToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((ushort)valueToConvert) != default(ushort);

        private static bool ConvertUInt32ToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((uint)valueToConvert) != default(uint);

        private static bool ConvertUInt64ToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((ulong)valueToConvert) != default(ulong);

        private static bool ConvertSByteToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((sbyte)valueToConvert) != default(sbyte);

        private static bool ConvertByteToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((byte)valueToConvert) != default(byte);

        private static bool ConvertSingleToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((float)valueToConvert) != default(float);

        private static bool ConvertDoubleToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((double)valueToConvert) != default(double);

        private static bool ConvertDecimalToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => ((decimal)valueToConvert) != default(decimal);

        private static PSConverter<bool> CreateNumericToBoolConverter(Type fromType)
        {
            Diagnostics.Assert(IsNumeric(fromType.GetTypeCode()), "Can only convert numeric types");

            ParameterExpression valueToConvert = Expression.Parameter(typeof(object));
            var parameters = new ParameterExpression[]
            {
                valueToConvert,
                Expression.Parameter(typeof(Type)),
                Expression.Parameter(typeof(bool)),
                Expression.Parameter(typeof(PSObject)),
                Expression.Parameter(typeof(IFormatProvider)),
                Expression.Parameter(typeof(TypeTable))
            };

            return Expression.Lambda<PSConverter<bool>>(
                Expression.NotEqual(Expression.Default(fromType), valueToConvert.Cast(fromType)),
                parameters).Compile();
        }

        private static bool ConvertCharToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting char to boolean.");
            return ((char)valueToConvert) != '\0';
        }

        private static bool ConvertSwitchParameterToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting SwitchParameter to boolean.");
            return ((SwitchParameter)valueToConvert).ToBool();
        }

        private static bool ConvertIListToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting IList to boolean.");
            return IsTrue((IList)valueToConvert);
        }

        private static string ConvertNumericToString(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            if (originalValueToConvert?.TokenText != null)
            {
                return originalValueToConvert.TokenText;
            }

            typeConversion.WriteLine("Converting numeric to string.");

            try
            {
                // Ignore formatProvider here, the conversion should be culture invariant.
                NumberFormatInfo numberFormat = CultureInfo.InvariantCulture.NumberFormat;
                if (valueToConvert is double dbl)
                {
                    return dbl.ToString(DoublePrecision, numberFormat);
                }

                if (valueToConvert is float sgl)
                {
                    return sgl.ToString(SinglePrecision, numberFormat);
                }

                return (string)Convert.ChangeType(
                    valueToConvert,
                    resultType,
                    CultureInfo.InvariantCulture.NumberFormat);
            }
            catch (Exception e)
            {
                typeConversion.WriteLine("Converting numeric to string Exception: \"{0}\".", e.Message);
                throw new PSInvalidCastException(
                    "InvalidCastFromNumericToString",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    e.Message);
            }
        }

        private static string ConvertNonNumericToString(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            ExecutionContext ecFromTLS = LocalPipeline.GetExecutionContextFromTLS();
            try
            {
                typeConversion.WriteLine("Converting object to string.");
                return PSObject.ToStringParser(ecFromTLS, valueToConvert, formatProvider);
            }
            catch (ExtendedTypeSystemException e)
            {
                typeConversion.WriteLine("Converting object to string Exception: \"{0}\".", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastFromAnyTypeToString",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastCannotRetrieveString);
            }
        }

        private static Hashtable ConvertIDictionaryToHashtable(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting to Hashtable.");
            return new Hashtable(valueToConvert as IDictionary);
        }

        private static PSReference ConvertToPSReference(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting to PSReference.");

            Diagnostics.Assert(
                valueToConvert != null,
                "[ref]$null cast should be handler earlier with a separate ConvertNullToPSReference method");

            return PSReference.CreateInstance(valueToConvert, valueToConvert.GetType());
        }

        private static Delegate ConvertScriptBlockToDelegate(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            Exception exception = null;

            try
            {
                return ((ScriptBlock)valueToConvert).GetDelegate(resultType);
            }
            catch (ArgumentNullException e)
            {
                exception = e;
            }
            catch (ArgumentException e)
            {
                exception = e;
            }
            catch (MissingMethodException e)
            {
                exception = e;
            }
            catch (MemberAccessException e)
            {
                exception = e;
            }
            finally
            {
                typeConversion.WriteLine("Converting script block to delegate Exception: \"{0}\".", exception.Message);

                throw new PSInvalidCastException(
                    "InvalidCastFromScriptBlockToDelegate",
                    exception,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    exception.Message);
            }
        }

        private static object ConvertToNullable(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                // The CLR doesn't support boxed Nullable<T>.  Instead, languages convert to T and box.
                => ConvertTo(
                    valueToConvert,
                    Nullable.GetUnderlyingType(resultType),
                    recursion,
                    formatProvider,
                    backupTable);

        private static object ConvertRelatedArrays(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine(
                "The element type of result is assignable from the element type of the value to convert");

            var originalAsArray = (Array)valueToConvert;
            var newValue = Array.CreateInstance(resultType.GetElementType(), originalAsArray.Length);
            originalAsArray.CopyTo(newValue, index: 0);

            return newValue;
        }

        private static object ConvertUnrelatedArrays(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            var valueAsArray = valueToConvert as Array;
            Type resultElementType = resultType.GetElementType();
            var resultArray = Array.CreateInstance(resultElementType, valueAsArray.Length);

            for (int i = 0; i < valueAsArray.Length; i++)
            {
                object resultElement = ConvertTo(
                    valueAsArray.GetValue(i),
                    resultElementType,
                    recursion: false,
                    formatProvider,
                    backupTable);

                resultArray.SetValue(resultElement, i);
            }

            return resultArray;
        }

        private static object ConvertEnumerableToArray(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            try
            {
                Type resultElementType = resultType == typeof(Array) ? typeof(object) : resultType.GetElementType();

                typeConversion.WriteLine("Converting elements in the value to convert to the result's element type.");

                var result = new ArrayList();
                foreach (object obj in GetEnumerable(valueToConvert))
                {
                    // Stop further recursion here
                    result.Add(ConvertTo(obj, resultElementType, recursion: false, formatProvider, backupTable));
                }

                return result.ToArray(resultElementType);
            }
            catch (Exception e)
            {
                typeConversion.WriteLine("Element conversion exception: \"{0}\".", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastExceptionEnumerableToArray",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    e.Message);
            }
        }

        private static object ConvertScalarToArray(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Value to convert is scalar.");

            if (originalValueToConvert != null && originalValueToConvert.TokenText != null)
            {
                valueToConvert = originalValueToConvert;
            }

            try
            {
                Type resultElementType = resultType == typeof(Array) ? typeof(object) : resultType.GetElementType();
                var result = new ArrayList
                {
                    // Stop further recursion here
                    ConvertTo(valueToConvert, resultElementType, recursion: false, formatProvider, backupTable)
                };

                return result.ToArray(resultElementType);
            }
            catch (Exception e)
            {
                typeConversion.WriteLine("Element conversion exception: \"{0}\".", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastExceptionScalarToArray",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    e.Message);
            }
        }

        private static object ConvertIntegerToEnum(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            object result;

            try
            {
                result = Enum.ToObject(resultType, valueToConvert);
            }
            catch (Exception e)
            {
                typeConversion.WriteLine("Integer to System.Enum exception: \"{0}\".", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastExceptionIntegerToEnum",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    e.Message);
            }

            // Check if the result is a defined enum, otherwise throw an error.
            // valueToConvert is the user supplied value. Use that in the error message.
            EnumSingleTypeConverter.ThrowForUndefinedEnum("UndefinedIntegerToEnum", result, valueToConvert, resultType);

            return result;
        }

        private static object ConvertStringToEnum(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            string valueAsString = valueToConvert as string;
            object result = null;

            typeConversion.WriteLine("Calling case sensitive Enum.Parse");
            try
            {
                result = Enum.Parse(resultType, valueAsString);
            }
            catch (ArgumentException e)
            {
                typeConversion.WriteLine("Enum.Parse Exception: \"{0}\".", e.Message);
                // Enum.Parse will always throw this kind of exception.
                // Even when no map exists. We want to try without case sensitivity
                // If it works, we will return it, otherwise a new exception will
                // be thrown and we will use it to set exceptionToWrap
                try
                {
                    typeConversion.WriteLine("Calling case insensitive Enum.Parse");
                    result = Enum.Parse(resultType, valueAsString, true);
                }
                catch (ArgumentException inner)
                {
                    typeConversion.WriteLine("Enum.Parse Exception: \"{0}\".", inner.Message);
                }
                catch (Exception ex) // Enum.Parse might throw unadvertised exceptions
                {
                    typeConversion.WriteLine("Case insensitive Enum.Parse threw an exception.");

                    throw new PSInvalidCastException(
                        "CaseInsensitiveEnumParseThrewAnException",
                        innerException: ex,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        ex.Message);
                }
            }
            catch (Exception e) // Enum.Parse might throw unadvertised exceptions
            {
                typeConversion.WriteLine("Case Sensitive Enum.Parse threw an exception.");

                throw new PSInvalidCastException(
                    "CaseSensitiveEnumParseThrewAnException",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    e.Message);
            }

            if (result == null)
            {
                typeConversion.WriteLine("Calling substring disambiguation.");
                try
                {
                    string enumValue = EnumMinimumDisambiguation.EnumDisambiguate(valueAsString, resultType);
                    result = Enum.Parse(resultType, enumValue);
                }
                // Wrap exceptions in type conversion exceptions
                catch (Exception e)
                {
                    typeConversion.WriteLine("Substring disambiguation threw an exception.");

                    throw new PSInvalidCastException(
                        "SubstringDisambiguationEnumParseThrewAnException",
                        innerException: e,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        e.Message);
                }
            }

            EnumSingleTypeConverter.ThrowForUndefinedEnum("EnumParseUndefined", result, valueToConvert, resultType);
            s_tracer.WriteLine("returning \"{0}\" from conversion to Enum.", result);

            return result;
        }

        private static object ConvertEnumerableToEnum(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            IEnumerator enumerator = GetEnumerator(valueToConvert);
            StringBuilder sbResult = new StringBuilder();
            bool notFirst = false;

            while (ParserOps.MoveNext(context: null, errorPosition: null, enumerator))
            {
                if (notFirst)
                {
                    sbResult.Append(',');
                }
                else
                {
                    notFirst = true;
                }

                string current = enumerator.Current as string;
                if (current == null)
                {
                    // If the object wasn't a string, then we'll try and convert it into an enum value,
                    // then convert the enum back to a string and finally append it to the string builder to
                    // preserve consistent semantics between quoted and unquoted lists...
                    object tempResult = ConvertTo(
                        enumerator.Current,
                        resultType, recursion,
                        formatProvider,
                        backupTable);

                    if (tempResult != null)
                    {
                        sbResult.Append(tempResult.ToString());
                    }
                    else
                    {
                        throw new PSInvalidCastException(
                            "InvalidCastEnumStringNotFound",
                            innerException: null,
                            ExtendedTypeSystem.InvalidCastExceptionEnumerationNoValue,
                            enumerator.Current,
                            resultType,
                            EnumSingleTypeConverter.EnumValues(resultType));
                    }
                }

                sbResult.Append(current);
            }

            return ConvertStringToEnum(
                sbResult.ToString(),
                resultType,
                recursion,
                originalValueToConvert,
                formatProvider,
                backupTable);
        }

        private class PSMethodToDelegateConverter
        {
            // Index of the matching overload method.
            private readonly int _matchIndex;
            // Size of the cache. It's rare to have more than 10 overloads for a method.
            private const int CacheSize = 10;
            private static readonly PSMethodToDelegateConverter[] s_converterCache =
                new PSMethodToDelegateConverter[CacheSize];

            private PSMethodToDelegateConverter(int matchIndex)
            {
                _matchIndex = matchIndex;
            }

            internal static PSMethodToDelegateConverter GetConverter(int matchIndex)
            {
                if (matchIndex >= CacheSize)
                {
                    return new PSMethodToDelegateConverter(matchIndex);
                }

                PSMethodToDelegateConverter result = s_converterCache[matchIndex];
                if (result == null)
                {
                    // If the cache entry is null, generate a new instance for the cache slot.
                    var converter = new PSMethodToDelegateConverter(matchIndex);
                    Threading.Interlocked.CompareExchange(ref s_converterCache[matchIndex], converter, null);
                    result = s_converterCache[matchIndex];
                }

                return result;
            }

            internal Delegate Convert(
                object valueToConvert,
                Type resultType,
                bool recursion,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable)
            {
                // We can only possibly convert PSMethod instance of the type PSMethod<T>.
                // Such a PSMethod essentially represents a set of .NET method overloads.
                var psMethod = (PSMethod)valueToConvert;

                try
                {
                    var methods = (MethodCacheEntry)psMethod.adapterData;
                    var isStatic = psMethod.instance is Type;
                    var candidate = (MethodInfo)methods.methodInformationStructures[_matchIndex].method;

                    return isStatic
                        ? candidate.CreateDelegate(resultType)
                        : candidate.CreateDelegate(resultType, psMethod.instance);
                }
                catch (Exception e)
                {
                    typeConversion.WriteLine("PSMethod to Delegate exception: \"{0}\".", e.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastExceptionPSMethodToDelegate",
                        innerException: e,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        e.Message);
                }
            }
        }

        private class ConvertViaParseMethod
        {
            // TODO - use an ETS wrapper that generates a dynamic method
            internal MethodInfo parse;

            internal object ConvertWithCulture(
                object valueToConvert,
                Type resultType,
                bool recursion,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable)
            {
                try
                {
                    object result = parse.Invoke(null, new object[2] { valueToConvert, formatProvider });
                    typeConversion.WriteLine("Parse result: {0}", result);

                    return result;
                }
                catch (TargetInvocationException ex)
                {
                    Exception innerException = ex.InnerException ?? ex;
                    typeConversion.WriteLine(
                        "Exception calling Parse method with CultureInfo: \"{0}\".",
                        innerException.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastParseTargetInvocationWithFormatProvider",
                        innerException,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        innerException.Message);
                }
                catch (Exception e)
                {
                    typeConversion.WriteLine("Exception calling Parse method with CultureInfo: \"{0}\".", e.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastParseExceptionWithFormatProvider",
                        innerException: e,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        e.Message);
                }
            }

            internal object ConvertWithoutCulture(
                object valueToConvert,
                Type resultType,
                bool recursion,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable)
            {
                try
                {
                    object result = parse.Invoke(null, new object[1] { valueToConvert });
                    typeConversion.WriteLine("Parse result: \"{0}\".", result);

                    return result;
                }
                catch (TargetInvocationException ex)
                {
                    Exception innerException = ex.InnerException ?? ex;
                    typeConversion.WriteLine("Exception calling Parse method: \"{0}\".", innerException.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastParseTargetInvocation",
                        innerException,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        innerException.Message);
                }
                catch (Exception e)
                {
                    typeConversion.WriteLine("Exception calling Parse method: \"{0}\".", e.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastParseException",
                        innerException: e,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        e.Message);
                }
            }
        }

        private class ConvertViaConstructor
        {
            internal Func<object, object> TargetCtorLambda;

            internal object Convert(
                object valueToConvert,
                Type resultType,
                bool recursion,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable)
            {
                try
                {
                    object result = TargetCtorLambda(valueToConvert);
                    typeConversion.WriteLine("Constructor result: \"{0}\".", result);

                    return result;
                }
                catch (TargetInvocationException ex)
                {
                    Exception innerException = ex.InnerException ?? ex;
                    typeConversion.WriteLine("Exception invoking Constructor: \"{0}\".", innerException.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastConstructorTargetInvocationException",
                        innerException,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        innerException.Message);
                }
                catch (Exception e)
                {
                    typeConversion.WriteLine("Exception invoking Constructor: \"{0}\".", e.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastConstructorException",
                        innerException: e,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        e.Message);
                }
            }
        }

        /// <summary>
        /// Create a IList to hold all elements, and use the IList to create the object of the resultType.
        /// The reason for using IList is that it can work on constructors that takes IEnumerable[T], ICollection[T] or IList[T].
        /// </summary>
        /// <remark>
        /// When get to this method, we know the fromType and the toType meet the following two conditions:
        /// 1. toType is a closed generic type and it has a constructor that takes IEnumerable[T], ICollection[T] or IList[T]
        /// 2. fromType is System.Array, System.Object[] or it's the same as the element type of toType
        /// </remark>
        private class ConvertViaIEnumerableConstructor
        {
            internal Func<int, IList> ListCtorLambda;
            internal Func<IList, object> TargetCtorLambda;

            internal Type ElementType;
            internal bool IsScalar;

            internal object Convert(
                object valueToConvert,
                Type resultType,
                bool recursion,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable)
            {
                IList resultAsList = null;
                Array array = null;

                try
                {
                    int listSize = 0;
                    if (IsScalar)
                    {
                        listSize = 1;
                    }
                    else
                    {
                        array = valueToConvert as Array;
                        listSize = array?.Length ?? 1;
                    }

                    resultAsList = ListCtorLambda(listSize);
                }
                catch (Exception)
                {
                    ThrowInvalidCastException(valueToConvert, resultType);
                    Diagnostics.Assert(false, "ThrowInvalidCastException always throws");

                    return null;
                }

                if (IsScalar)
                {
                    resultAsList.Add(valueToConvert);
                }
                else if (array == null)
                {
                    object convertedValue = ConvertTo(valueToConvert, ElementType, formatProvider);
                    resultAsList.Add(convertedValue);
                }
                else
                {
                    foreach (object item in array)
                    {
                        object baseObj = PSObject.Base(item);
                        object convertedValue;
                        if (!TryConvertTo(baseObj, ElementType, formatProvider, out convertedValue))
                        {
                            ThrowInvalidCastException(valueToConvert, resultType);
                            Diagnostics.Assert(false, "ThrowInvalidCastException always throws");

                            return null;
                        }
                        else
                        {
                            resultAsList.Add(convertedValue);
                        }
                    }
                }

                try
                {
                    object result = TargetCtorLambda(resultAsList);
                    typeConversion.WriteLine("IEnumerable Constructor result: \"{0}\".", result);

                    return result;
                }
                catch (TargetInvocationException ex)
                {
                    Exception innerException = ex.InnerException ?? ex;
                    typeConversion.WriteLine(
                        "Exception invoking IEnumerable Constructor: \"{0}\".",
                        innerException.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastConstructorTargetInvocationException",
                        innerException,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        innerException.Message);
                }
                catch (Exception e)
                {
                    typeConversion.WriteLine("Exception invoking IEnumerable Constructor: \"{0}\".", e.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastConstructorException",
                        innerException: e,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        e.Message);
                }
            }
        }

        private class ConvertViaNoArgumentConstructor
        {
            private readonly Func<object> _constructor;

            internal ConvertViaNoArgumentConstructor(ConstructorInfo constructor, Type type)
            {
                NewExpression newExpr = constructor != null
                    ? Expression.New(constructor)
                    : Expression.New(type);

                _constructor = Expression.Lambda<Func<object>>(newExpr.Cast(typeof(object))).Compile();
            }

            internal object Convert(
                object valueToConvert,
                Type resultType,
                bool recursion,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable)
                    => Convert(
                        valueToConvert,
                        resultType,
                        recursion,
                        originalValueToConvert,
                        formatProvider,
                        backupTable,
                        ignoreUnknownMembers: false);

            internal object Convert(
                object valueToConvert,
                Type resultType,
                bool recursion,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable,
                bool ignoreUnknownMembers)
            {
                try
                {
                    ExecutionContext ecFromTLS = LocalPipeline.GetExecutionContextFromTLS();
                    object result = null;

                    // Setting arbitrary properties is dangerous, so we allow this only if
                    //  - It's running on a thread without Runspace; Or
                    //  - It's in FullLanguage but not because it's part of a parameter binding that is transitioning from ConstrainedLanguage to FullLanguage
                    // When this is invoked from a parameter binding in transition from ConstrainedLanguage environment to FullLanguage command, we disallow
                    // the property conversion because it's dangerous.
                    if (ecFromTLS == null
                        || (ecFromTLS.LanguageMode == PSLanguageMode.FullLanguage
                            && !ecFromTLS.LanguageModeTransitionInParameterBinding))
                    {
                        result = _constructor();
                        var psobject = PSObject.AsPSObject(valueToConvert);
                        if (psobject != null)
                        {
                            // Use PSObject properties to perform conversion.
                            SetObjectProperties(
                                result,
                                psobject,
                                resultType,
                                CreateMemberNotFoundError,
                                CreateMemberSetValueError,
                                formatProvider,
                                recursion,
                                ignoreUnknownMembers);
                        }
                        else
                        {
                            // Use provided property dictionary to perform conversion.
                            // The method invocation is disabled for "Hashtable to Object conversion" (Win8:649519),
                            // but we need to keep it enabled for New-Object for compatibility to PSv2
                            IDictionary properties = valueToConvert as IDictionary;
                            SetObjectProperties(
                                result,
                                properties,
                                resultType,
                                CreateMemberNotFoundError,
                                CreateMemberSetValueError,
                                enableMethodCall: false);
                        }

                        typeConversion.WriteLine("Constructor result: \"{0}\".", result);
                    }
                    else
                    {
                        throw InterpreterError.NewInterpreterException(
                            valueToConvert,
                            typeof(RuntimeException),
                            errorPosition: null,
                            "HashtableToObjectConversionNotSupportedInDataSection",
                            ParserStrings.HashtableToObjectConversionNotSupportedInDataSection,
                            resultType.ToString());
                    }

                    return result;
                }
                catch (TargetInvocationException ex)
                {
                    Exception innerException = ex.InnerException ?? ex;
                    typeConversion.WriteLine("Exception invoking Constructor: \"{0}\".", innerException.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastConstructorTargetInvocationException",
                        innerException,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        innerException.Message);
                }
                catch (InvalidOperationException e)
                {
                    Exception innerException = e.InnerException ?? e;

                    throw new PSInvalidCastException(
                        "ObjectCreationError",
                        innerException: e,
                        ExtendedTypeSystem.ObjectCreationError,
                        resultType.ToString(),
                        innerException.Message);
                }
                catch (SetValueException e)
                {
                    Exception innerException = e.InnerException ?? e;

                    throw new PSInvalidCastException(
                        "ObjectCreationError",
                        innerException,
                        ExtendedTypeSystem.ObjectCreationError,
                        resultType.ToString(),
                        innerException.Message);
                }
                catch (RuntimeException ex)
                {
                    Exception innerException = ex.InnerException ?? ex;
                    typeConversion.WriteLine("Exception invoking Constructor: \"{0}\".", innerException.Message);

                    throw new PSInvalidCastException(
                        "InvalidCastConstructorException",
                        innerException,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        innerException.Message);
                }
                catch (Exception e)
                {
                    typeConversion.WriteLine("Exception invoking Constructor: \"{0}\".", e.Message);
                    throw new PSInvalidCastException(
                        "InvalidCastConstructorException",
                        innerException: e,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        e.Message);
                }
            }
        }

        private class ConvertViaCast
        {
            internal MethodInfo cast;

            internal object Convert(
                object valueToConvert,
                Type resultType,
                bool recursion,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable)
            {
                try
                {
                    return cast.Invoke(null, new object[1] { valueToConvert });
                }
                catch (TargetInvocationException ex)
                {
                    Exception innerException = ex.InnerException ?? ex;
                    typeConversion.WriteLine("Cast operator exception: \"{0}\".", innerException.Message);

                    throw new PSInvalidCastException(
                        $"InvalidCastTargetInvocationException{cast.Name}",
                        innerException,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        innerException.Message);
                }
                catch (Exception e)
                {
                    typeConversion.WriteLine("Cast operator exception: \"{0}\".", e.Message);

                    throw new PSInvalidCastException(
                        $"InvalidCastException{cast.Name}",
                        innerException: e,
                        ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                        valueToConvert.ToString(),
                        resultType.ToString(),
                        e.Message);
                }
            }
        }

        private static object ConvertIConvertible(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            try
            {
                object result = Convert.ChangeType(valueToConvert, resultType, formatProvider);
                typeConversion.WriteLine("Conversion using IConvertible succeeded.");

                return result;
            }
            catch (Exception e)
            {
                typeConversion.WriteLine("Exception converting with IConvertible: \"{0}\".", e.Message);

                throw new PSInvalidCastException(
                    "InvalidCastIConvertible",
                    innerException: e,
                    ExtendedTypeSystem.InvalidCastExceptionWithInnerException,
                    valueToConvert.ToString(),
                    resultType.ToString(),
                    e.Message);
            }
        }

        private static object ConvertNumericIConvertible(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            // If the original object was a number, then try and do a conversion on the string
            // equivalent of that number...
            if (originalValueToConvert?.TokenText != null)
            {
                return ConvertTo(
                    originalValueToConvert.TokenText,
                    resultType,
                    recursion,
                    formatProvider,
                    backupTable);
            }
            else
            {
                // Convert the source object to a string...
                string sourceAsString = (string)ConvertTo(
                    valueToConvert,
                    typeof(string),
                    recursion,
                    formatProvider,
                    backupTable);

                // And try and convert that string to the target type...
                return ConvertTo(
                    sourceAsString,
                    resultType,
                    recursion,
                    formatProvider,
                    backupTable);
            }
        }

        private class ConvertCheckingForCustomConverter
        {
            internal PSConverter<object> tryfirstConverter;
            internal PSConverter<object> fallbackConverter;

            internal object Convert(
                object valueToConvert,
                Type resultType,
                bool recursion,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable)
            {
                if (tryfirstConverter != null)
                {
                    try
                    {
                        return tryfirstConverter(
                            valueToConvert,
                            resultType,
                            recursion,
                            originalValueToConvert,
                            formatProvider,
                            backupTable);
                    }
                    catch (InvalidCastException)
                    {
                    }
                }

                if (IsCustomTypeConversion(
                    originalValueToConvert ?? valueToConvert,
                    resultType,
                    formatProvider,
                    out object result,
                    backupTable))
                {
                    typeConversion.WriteLine("Custom Type Conversion succeeded.");
                    return result;
                }

                if (fallbackConverter != null)
                {
                    return fallbackConverter(
                        valueToConvert,
                        resultType,
                        recursion,
                        originalValueToConvert,
                        formatProvider,
                        backupTable);
                }

                throw new PSInvalidCastException(
                    "ConvertToFinalInvalidCastException",
                    innerException: null,
                    ExtendedTypeSystem.InvalidCastException,
                    valueToConvert.ToString(),
                    ObjectToTypeNameString(valueToConvert),
                    resultType.ToString());
            }
        }

        #region Delegates converting null
        private static object ConvertNullToNumeric(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting null to zero.");

            // If the destination type is numeric, convert 0 to resultType
            return Convert.ChangeType(value: 0, resultType, CultureInfo.InvariantCulture);
        }

        private static char ConvertNullToChar(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting null to '0'.");
            return '\0';
        }

        private static string ConvertNullToString(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting null to \"\".");

            // if the destination type is string, return an empty string...
            return string.Empty;
        }

        private static PSReference ConvertNullToPSReference(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => new PSReference<Null>(null);

        private static object ConvertNullToRef(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            // if the target type is not a value type, return the original
            // "null" object. Don't just return null because we want to preserve
            // an msh object if possible.
            return valueToConvert;
        }

        private static bool ConvertNullToBool(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting null to boolean.");
            return false;
        }

        private static object ConvertNullToNullable(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
                => null;

        private static SwitchParameter ConvertNullToSwitch(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting null to SwitchParameter(false).");
            return new SwitchParameter(isPresent: false);
        }

        private static object ConvertNullToVoid(
            object valueToConvert,
            Type resultType,
            bool recursion,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            typeConversion.WriteLine("Converting null to AutomationNull.Value.");
            return AutomationNull.Value;
        }

        #endregion Delegates converting null

        private static object ConvertNoConversion(
            object valueToConvert,
            Type resultType,
            bool recurse,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            ThrowInvalidCastException(valueToConvert, resultType);
            Diagnostics.Assert(false, "ThrowInvalidCastException always throws");

            return null;
        }

        private static object ConvertNotSupportedConversion(
            object valueToConvert,
            Type resultType,
            bool recurse,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable)
        {
            ThrowInvalidConversionException(valueToConvert, resultType);
            Diagnostics.Assert(false, "ThrowInvalidCastException always throws");

            return null;
        }

        [System.Diagnostics.DebuggerDisplay("{from.Name}->{to.Name}")]
        private struct ConversionTypePair
        {
            internal Type from;
            internal Type to;

            internal ConversionTypePair(Type fromType, Type toType)
            {
                from = fromType;
                to = toType;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    // To prevent to/from == from/to, multiply and add rather than use
                    // an operation that won't overflow, like bitwise xor.
                    return from.GetHashCode() + 37 * to.GetHashCode();
                }
            }

            public override bool Equals(object other)
            {
                if (!(other is ConversionTypePair))
                {
                    return false;
                }

                var ctp = (ConversionTypePair)other;
                return from == ctp.from && to == ctp.to;
            }
        }

        internal delegate T PSConverter<T>(
            object valueToConvert,
            Type resultType,
            bool recurse,
            PSObject originalValueToConvert,
            IFormatProvider formatProvider,
            TypeTable backupTable);

        internal delegate object PSNullConverter(object nullOrAutomationNull);

        internal interface IConversionData
        {
            object Converter { get; }

            ConversionRank Rank { get; }

            object Invoke(
                object valueToConvert,
                Type resultType,
                bool recurse,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable);
        }

        [System.Diagnostics.DebuggerDisplay("{_converter.Method.Name}")]
        internal class ConversionData<T> : IConversionData
        {
            private readonly PSConverter<T> _converter;

            public ConversionData(PSConverter<T> converter, ConversionRank rank)
            {
                _converter = converter;
                Rank = rank;
            }

            public object Converter { get => _converter; }

            public ConversionRank Rank { get; }

            public object Invoke(
                object valueToConvert,
                Type resultType,
                bool recurse,
                PSObject originalValueToConvert,
                IFormatProvider formatProvider,
                TypeTable backupTable)
                    => _converter.Invoke(
                        valueToConvert,
                        resultType,
                        recurse,
                        originalValueToConvert,
                        formatProvider,
                        backupTable);
        }

        private static readonly Dictionary<ConversionTypePair, IConversionData> s_converterCache =
            new Dictionary<ConversionTypePair, IConversionData>(256);

        private static IConversionData CacheConversion<T>(
            Type fromType,
            Type toType,
            PSConverter<T> converter,
            ConversionRank rank)
        {
            var pair = new ConversionTypePair(fromType, toType);
            IConversionData data = null;

            lock (s_converterCache)
            {
                if (!s_converterCache.TryGetValue(pair, out data))
                {
                    data = new ConversionData<T>(converter, rank);
                    s_converterCache.Add(pair, data);
                }
                else
                {
                    Diagnostics.Assert(
                        ((Delegate)data.Converter).GetMethodInfo().Equals(converter.GetMethodInfo()),
                        "Existing conversion isn't the same as new conversion");
                }
            }

            return data;
        }

        private static IConversionData GetConversionData(Type fromType, Type toType)
        {
            lock (s_converterCache)
            {
                s_converterCache.TryGetValue(new ConversionTypePair(fromType, toType), out IConversionData result);
                return result;
            }
        }

        internal static ConversionRank GetConversionRank(Type fromType, Type toType)
            => FigureConversion(fromType, toType).Rank;

        private static readonly Type[] s_numericTypes = new Type[] {
            typeof(short), typeof(int), typeof(long),
            typeof(ushort), typeof(uint), typeof(ulong),
            typeof(sbyte), typeof(byte),
            typeof(float), typeof(double), typeof(decimal)
        };

        private static readonly Type[] s_integerTypes = new Type[] {
            typeof(short), typeof(int), typeof(long),
            typeof(ushort), typeof(uint), typeof(ulong),
            typeof(sbyte), typeof(byte)
        };

        // Do not reorder the elements of these arrays, we depend on them being ordered by increasing size.
        private static readonly Type[] s_signedIntegerTypes = new Type[] {
            typeof(sbyte), typeof(short), typeof(int), typeof(long)
        };
        private static readonly Type[] s_unsignedIntegerTypes = new Type[] {
            typeof(byte), typeof(ushort), typeof(uint), typeof(ulong)
        };

        private static readonly Type[] s_realTypes = new Type[] { typeof(float), typeof(double), typeof(decimal) };

        internal static void RebuildConversionCache()
        {
            lock (s_converterCache)
            {
                s_converterCache.Clear();

                Type typeofString = typeof(string);
                Type typeofNull = typeof(Null);
                Type typeofFloat = typeof(float);
                Type typeofDouble = typeof(double);
                Type typeofDecimal = typeof(decimal);
                Type typeofBool = typeof(bool);
                Type typeofChar = typeof(char);

                foreach (Type type in s_numericTypes)
                {
                    CacheConversion<string>(type, typeofString, ConvertNumericToString, ConversionRank.NumericString);
                    CacheConversion<object>(type, typeofChar, ConvertIConvertible, ConversionRank.NumericString);
                    CacheConversion<object>(typeofNull, type, ConvertNullToNumeric, ConversionRank.NullToValue);
                }

                CacheConversion<bool>(typeof(short), typeofBool, ConvertInt16ToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(int), typeofBool, ConvertInt32ToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(long), typeofBool, ConvertInt64ToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(ushort), typeofBool, ConvertUInt16ToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(uint), typeofBool, ConvertUInt32ToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(ulong), typeofBool, ConvertUInt64ToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(sbyte), typeofBool, ConvertSByteToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(byte), typeofBool, ConvertByteToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(float), typeofBool, ConvertSingleToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(double), typeofBool, ConvertDoubleToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(decimal), typeofBool, ConvertDecimalToBool, ConversionRank.Language);

                for (int i = 0; i < s_unsignedIntegerTypes.Length; i++)
                {
                    // Identical types are an identity conversion.
                    CacheConversion<object>(
                        s_unsignedIntegerTypes[i],
                        s_unsignedIntegerTypes[i],
                        ConvertAssignableFrom,
                        ConversionRank.Identity);
                    CacheConversion<object>(
                        s_signedIntegerTypes[i],
                        s_signedIntegerTypes[i],
                        ConvertAssignableFrom,
                        ConversionRank.Identity);

                    // Unsigned to signed same size is explicit
                    CacheConversion<object>(
                        s_unsignedIntegerTypes[i],
                        s_signedIntegerTypes[i],
                        ConvertNumeric,
                        ConversionRank.NumericExplicit);
                    // Signed to unsigned same size is explicit, but better than the reverse (because it is "more specific")
                    CacheConversion<object>(
                        s_signedIntegerTypes[i],
                        s_unsignedIntegerTypes[i],
                        ConvertNumeric,
                        ConversionRank.NumericExplicit1);

                    for (int j = i + 1; j < s_unsignedIntegerTypes.Length; j++)
                    {
                        // Conversions where the sign doesn't change, but the size is bigger, is implicit
                        CacheConversion<object>(
                            s_unsignedIntegerTypes[i],
                            s_unsignedIntegerTypes[j],
                            ConvertNumeric,
                            ConversionRank.NumericImplicit);
                        CacheConversion<object>(
                            s_signedIntegerTypes[i],
                            s_signedIntegerTypes[j],
                            ConvertNumeric,
                            ConversionRank.NumericImplicit);

                        // Conversion from smaller unsigned to bigger signed is implicit
                        CacheConversion<object>(
                            s_unsignedIntegerTypes[i],
                            s_signedIntegerTypes[j],
                            ConvertNumeric,
                            ConversionRank.NumericImplicit);
                        // Conversion from smaller signed to bigger unsigned is the "better" explicit conversion
                        CacheConversion<object>(
                            s_signedIntegerTypes[i],
                            s_unsignedIntegerTypes[j],
                            ConvertNumeric,
                            ConversionRank.NumericExplicit1);

                        // Conversion to a smaller type is explicit
                        CacheConversion<object>(
                            s_unsignedIntegerTypes[j],
                            s_unsignedIntegerTypes[i],
                            ConvertNumeric,
                            ConversionRank.NumericExplicit);
                        CacheConversion<object>(
                            s_signedIntegerTypes[j],
                            s_signedIntegerTypes[i],
                            ConvertNumeric,
                            ConversionRank.NumericExplicit);
                        CacheConversion<object>(
                            s_unsignedIntegerTypes[j],
                            s_signedIntegerTypes[i],
                            ConvertNumeric,
                            ConversionRank.NumericExplicit);
                        CacheConversion<object>(
                            s_signedIntegerTypes[j],
                            s_unsignedIntegerTypes[i],
                            ConvertNumeric,
                            ConversionRank.NumericExplicit);
                    }
                }

                foreach (Type integerType in s_integerTypes)
                {
                    CacheConversion<object>(
                        typeofString,
                        integerType,
                        ConvertStringToInteger,
                        ConversionRank.NumericString);

                    foreach (Type realType in s_realTypes)
                    {
                        CacheConversion<object>(
                            integerType,
                            realType,
                            ConvertNumeric,
                            ConversionRank.NumericImplicit);
                        CacheConversion<object>(
                            realType,
                            integerType,
                            ConvertNumeric,
                            ConversionRank.NumericExplicit);
                    }
                }

                CacheConversion<object>(typeofFloat, typeofDouble, ConvertNumeric, ConversionRank.NumericImplicit);
                CacheConversion<object>(typeofDouble, typeofFloat, ConvertNumeric, ConversionRank.NumericExplicit);
                CacheConversion<object>(typeofFloat, typeofDecimal, ConvertNumeric, ConversionRank.NumericExplicit);
                CacheConversion<object>(typeofDouble, typeofDecimal, ConvertNumeric, ConversionRank.NumericExplicit);
                CacheConversion<object>(typeofDecimal, typeofFloat, ConvertNumeric, ConversionRank.NumericExplicit1);
                CacheConversion<object>(typeofDecimal, typeofDouble, ConvertNumeric, ConversionRank.NumericExplicit1);

                CacheConversion<Regex>(typeofString, typeof(Regex), ConvertStringToRegex, ConversionRank.Language);
                CacheConversion<char[]>(typeofString, typeof(char[]), ConvertStringToCharArray, ConversionRank.StringToCharArray);
                CacheConversion<Type>(typeofString, typeof(Type), ConvertStringToType, ConversionRank.Language);
                CacheConversion<Uri>(typeofString, typeof(Uri), ConvertStringToUri, ConversionRank.Language);
                CacheConversion<object>(typeofString, typeofDecimal, ConvertStringToDecimal, ConversionRank.NumericString);
                CacheConversion<object>(typeofString, typeofFloat, ConvertStringToReal, ConversionRank.NumericString);
                CacheConversion<object>(typeofString, typeofDouble, ConvertStringToReal, ConversionRank.NumericString);
                CacheConversion<object>(typeofChar, typeofFloat, ConvertNumericChar, ConversionRank.Language);
                CacheConversion<object>(typeofChar, typeofDouble, ConvertNumericChar, ConversionRank.Language);
                CacheConversion<bool>(typeofChar, typeofBool, ConvertCharToBool, ConversionRank.Language);

                // Conversions from null
                CacheConversion<char>(typeofNull, typeofChar, ConvertNullToChar, ConversionRank.NullToValue);
                CacheConversion<string>(typeofNull, typeofString, ConvertNullToString, ConversionRank.ToString);
                CacheConversion<bool>(typeofNull, typeofBool, ConvertNullToBool, ConversionRank.NullToValue);
                CacheConversion<PSReference>(typeofNull, typeof(PSReference), ConvertNullToPSReference, ConversionRank.NullToRef);
                CacheConversion<SwitchParameter>(typeofNull, typeof(SwitchParameter), ConvertNullToSwitch, ConversionRank.NullToValue);
                CacheConversion<object>(typeofNull, typeof(void), ConvertNullToVoid, ConversionRank.NullToValue);

                // Conversions to bool
                CacheConversion<object>(typeofBool, typeofBool, ConvertAssignableFrom, ConversionRank.Identity);
                CacheConversion<bool>(typeofString, typeofBool, ConvertStringToBool, ConversionRank.Language);
                CacheConversion<bool>(typeof(SwitchParameter), typeofBool, ConvertSwitchParameterToBool, ConversionRank.Language);

#if !UNIX
                // Conversions to WMI and ADSI
                CacheConversion<ManagementObjectSearcher>(
                    typeofString,
                    typeof(ManagementObjectSearcher),
                    ConvertToWMISearcher,
                    ConversionRank.Language);
                CacheConversion<ManagementClass>(
                    typeofString,
                    typeof(ManagementClass),
                    ConvertToWMIClass,
                    ConversionRank.Language);
                CacheConversion<ManagementObject>(
                    typeofString,
                    typeof(ManagementObject),
                    ConvertToWMI,
                    ConversionRank.Language);
                CacheConversion<DirectoryEntry>(
                    typeofString,
                    typeof(DirectoryEntry),
                    ConvertToADSI,
                    ConversionRank.Language);
                CacheConversion<DirectorySearcher>(
                    typeofString,
                    typeof(DirectorySearcher),
                    ConvertToADSISearcher,
                    ConversionRank.Language);
#endif
            }
        }

        internal static PSObject SetObjectProperties(
            object obj,
            PSObject psObject,
            Type resultType,
            MemberNotFoundError memberNotFoundErrorAction,
            MemberSetValueError memberSetValueErrorAction,
            IFormatProvider formatProvider,
            bool recursion = false,
            bool ignoreUnknownMembers = false)
        {
            // Type conversion from object properties only supported for deserialized types.
            if (Deserializer.IsDeserializedInstanceOfType(psObject, resultType))
            {
                try
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    foreach (PSPropertyInfo item in psObject.Properties)
                    {
                        if (item is PSProperty)
                        {
                            properties.Add(item.Name, item.Value);
                        }
                    }

                    // Win8:649519
                    return SetObjectProperties(
                        obj,
                        properties,
                        resultType,
                        memberNotFoundErrorAction,
                        memberSetValueErrorAction,
                        enableMethodCall: false);
                }
                catch (SetValueException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
            else
            {
                object baseObj = PSObject.Base(psObject);
                if (baseObj is IDictionary dictionary)
                {
                    // Win8:649519
                    return SetObjectProperties(
                        obj,
                        dictionary,
                        resultType,
                        memberNotFoundErrorAction,
                        memberSetValueErrorAction,
                        enableMethodCall: false);
                }
                else
                {
                    // Support PSObject to Strong type conversion.
                    if (baseObj is PSObject psBaseObject)
                    {
                        Dictionary<string, object> properties = new Dictionary<string, object>();
                        foreach (var item in psBaseObject.Properties)
                        {
                            properties.Add(item.Name, item.Value);
                        }

                        try
                        {
                            return SetObjectProperties(
                                obj,
                                properties,
                                resultType,
                                memberNotFoundErrorAction,
                                memberSetValueErrorAction,
                                enableMethodCall: false,
                                formatProvider,
                                recursion,
                                ignoreUnknownMembers);
                        }
                        catch (InvalidOperationException exception)
                        {
                            throw new PSInvalidCastException(
                                "ConvertToFinalInvalidCastException",
                                exception,
                                ExtendedTypeSystem.InvalidCastException,
                                psObject.ToString(),
                                ObjectToTypeNameString(psObject),
                                resultType.ToString());
                        }
                    }
                }
            }

            ThrowInvalidCastException(psObject, resultType);
            return null;
        }

        internal static PSObject SetObjectProperties(
            object obj,
            IDictionary properties,
            Type resultType,
            MemberNotFoundError memberNotFoundErrorAction,
            MemberSetValueError memberSetValueErrorAction,
            bool enableMethodCall)
                => SetObjectProperties(
                    obj,
                    properties,
                    resultType,
                    memberNotFoundErrorAction,
                    memberSetValueErrorAction,
                    enableMethodCall,
                    CultureInfo.InvariantCulture,
                    recursion: false,
                    ignoreUnknownMembers: false);

        internal static PSObject SetObjectProperties(
            object obj,
            IDictionary properties,
            Type resultType,
            MemberNotFoundError memberNotFoundErrorAction,
            MemberSetValueError memberSetValueErrorAction,
            bool enableMethodCall,
            IFormatProvider formatProvider,
            bool recursion = false,
            bool ignoreUnknownMembers = false)
        {
            PSObject pso = PSObject.AsPSObject(obj);
            if (properties != null)
            {
                foreach (DictionaryEntry prop in properties)
                {
                    PSMethodInfo method = enableMethodCall ? pso.Methods[prop.Key.ToString()] : null;
                    try
                    {
                        if (method != null)
                        {
                            method.Invoke(new object[] { prop.Value });
                        }
                        else
                        {
                            PSPropertyInfo property = pso.Properties[prop.Key.ToString()];
                            if (property != null)
                            {
                                object propValue = prop.Value;
                                if (recursion && prop.Value != null)
                                {
                                    if (TypeResolver.TryResolveType(property.TypeNameOfValue, out Type propType))
                                    {
                                        if (formatProvider == null)
                                        { formatProvider = CultureInfo.InvariantCulture; }

                                        try
                                        {
                                            if (prop.Value is PSObject propertyValue)
                                            {
                                                propValue = ConvertPSObjectToType(
                                                    propertyValue,
                                                    propType,
                                                    recursion,
                                                    formatProvider,
                                                    ignoreUnknownMembers);
                                            }
                                            else if (prop.Value is PSCustomObject)
                                            {
                                                propValue = ConvertPSObjectToType(
                                                    new PSObject(prop.Value),
                                                    propType,
                                                    recursion,
                                                    formatProvider,
                                                    ignoreUnknownMembers);
                                            }
                                            else
                                            {
                                                propValue = ConvertTo(
                                                    prop.Value,
                                                    propType,
                                                    recursion,
                                                    formatProvider,
                                                    backupTypeTable: null);
                                            }
                                        }
                                        catch (SetValueException)
                                        {
                                            // We don't care. We will assign the value as is.
                                        }
                                    }
                                }

                                property.Value = propValue;
                            }
                            else
                            {
                                if (pso.BaseObject is PSCustomObject)
                                {
                                    if (prop.Key is string key
                                        && prop.Value is string value
                                        && key.Equals("PSTypeName", StringComparison.OrdinalIgnoreCase))
                                    {
                                        pso.TypeNames.Insert(index: 0, value);
                                    }
                                    else
                                    {
                                        pso.Properties.Add(new PSNoteProperty(prop.Key.ToString(), prop.Value));
                                    }
                                }
                                else
                                {
                                    if (!ignoreUnknownMembers)
                                    {
                                        memberNotFoundErrorAction(pso, prop, resultType);
                                    }
                                }
                            }
                        }
                    }
                    catch (SetValueException e)
                    {
                        memberSetValueErrorAction(e);
                    }
                }
            }

            return pso;
        }

        private static string GetAvailableProperties(PSObject pso)
        {
            var availableProperties = new StringBuilder();
            bool first = true;

            if (pso?.Properties != null)
            {
                foreach (PSPropertyInfo property in pso.Properties)
                {
                    if (first == false)
                    {
                        availableProperties.Append(" , ");
                    }

                    availableProperties.Append($"[{property.Name} <{property.TypeNameOfValue}>]");
                    if (first == true)
                    {
                        first = false;
                    }
                }
            }

            return availableProperties.ToString();
        }

        internal static IConversionData FigureConversion(object valueToConvert, Type resultType, out bool debase)
        {
            PSObject valueAsPsObj;
            Type originalType;
            if (IsNull(valueToConvert))
            {
                valueAsPsObj = null;
                originalType = typeof(Null);
            }
            else
            {
                valueAsPsObj = valueToConvert as PSObject;
                originalType = valueToConvert.GetType();
            }

            debase = false;

            IConversionData data = FigureConversion(originalType, resultType);
            if (data.Rank != ConversionRank.None)
            {
                return data;
            }

            if (valueAsPsObj != null)
            {
                debase = true;

                // Now try converting PSObject.Base instead.
                valueToConvert = PSObject.Base(valueToConvert);

                Diagnostics.Assert(
                    valueToConvert != AutomationNull.Value,
                    "PSObject.Base converts AutomationNull.Value to null");

                if (valueToConvert == null)
                {
                    originalType = typeof(Null);
                }
                else
                {
                    // If the original value was a property bag (empty PSObject), we won't find a conversion because
                    // all PSObject conversions have already been checked.
                    //
                    // Still, there are many valid conversions to allow, such as PSObject to bool, void, or
                    // a custom type converter.  To find those, we consider InternalPSObject=>resultType instead.
                    //
                    // We use a different type because we can't keep PSObject as the from type in the cache.
                    originalType = valueToConvert is PSObject ? typeof(InternalPSObject) : valueToConvert.GetType();
                }

                data = FigureConversion(originalType, resultType);
            }

            return data;
        }

        /// <summary>
        /// </summary>
        /// <param name="valueToConvert">The same as in the public version.</param>
        /// <param name="resultType">The same as in the public version.</param>
        /// <param name="recursion">True if we should perform any recursive calls to ConvertTo.</param>
        /// <param name="formatProvider">Governing conversion of types.</param>
        /// <param name="backupTypeTable">
        /// Used by Remoting Rehydration Logic. While Deserializing a remote object,
        /// LocalPipeline.ExecutionContextFromTLS() might return null..In which case this
        /// TypeTable will be used to do the conversion.
        /// </param>
        /// <returns>The value converted.</returns>
        /// <exception cref="ArgumentNullException">If resultType is null.</exception>
        /// <exception cref="PSInvalidCastException">If the conversion failed.</exception>
        internal static object ConvertTo(
            object valueToConvert,
            Type resultType,
            bool recursion,
            IFormatProvider formatProvider,
            TypeTable backupTypeTable)
        {
            using (typeConversion.TraceScope("Converting \"{0}\" to \"{1}\".", valueToConvert, resultType))
            {
                if (resultType == null)
                {
                    throw PSTraceSource.NewArgumentNullException("resultType");
                }

                IConversionData conversion = FigureConversion(valueToConvert, resultType, out bool debase);

                return conversion.Invoke(
                    debase ? PSObject.Base(valueToConvert) : valueToConvert,
                    resultType,
                    recursion,
                    debase ? (PSObject)valueToConvert : null,
                    formatProvider,
                    backupTypeTable);
            }
        }

        /// <summary>
        /// Get the errorId and errorMessage for an InvalidCastException.
        /// </summary>
        /// <param name="valueToConvert"></param>
        /// <param name="resultType"></param>
        /// <returns>
        /// A two-element tuple indicating [errorId, errorMsg]
        /// </returns>
        internal static (string errorId, string message) GetInvalidCastMessages(
            object valueToConvert,
            Type resultType)
        {
            string errorId, errorMsg;
            if (resultType.IsByRefLike)
            {
                typeConversion.WriteLine("Cannot convert to ByRef-Like types as they should be used on stack only.");

                errorId = nameof(ExtendedTypeSystem.InvalidCastToByRefLikeType);
                errorMsg = StringUtil.Format(ExtendedTypeSystem.InvalidCastToByRefLikeType, resultType);

                return (errorId, errorMsg);
            }

            if (PSObject.Base(valueToConvert) == null)
            {
                if (resultType.IsEnum)
                {
                    typeConversion.WriteLine(
                        "Issuing an error message about not being able to convert null to an Enum type.");
                    // a nice error message specifically for null being converted to enum
                    errorId = "nullToEnumInvalidCast";
                    errorMsg = StringUtil.Format(
                        ExtendedTypeSystem.InvalidCastExceptionEnumerationNull,
                        resultType,
                        EnumSingleTypeConverter.EnumValues(resultType));

                    return (errorId, errorMsg);
                }

                typeConversion.WriteLine("Cannot convert null.");

                // finally throw of all other value types...
                errorId = "nullToObjectInvalidCast";
                errorMsg = StringUtil.Format(ExtendedTypeSystem.InvalidCastFromNull, resultType.ToString());

                return (errorId, errorMsg);
            }

            typeConversion.WriteLine("Type Conversion failed.");

            errorId = "ConvertToFinalInvalidCastException";
            errorMsg = StringUtil.Format(
                ExtendedTypeSystem.InvalidCastException,
                valueToConvert.ToString(),
                ObjectToTypeNameString(valueToConvert),
                resultType.ToString());

            return (errorId, errorMsg);
        }

        // Even though this never returns, expression trees expect non-void values in places, and so it's easier
        // to claim it returns object than to add extra expressions to keep the trees type safe.
        internal static object ThrowInvalidCastException(object valueToConvert, Type resultType)
        {
            // Get exception messages (in order): errorId, errorMsg
            (string errorId, string message) = GetInvalidCastMessages(valueToConvert, resultType);
            throw new PSInvalidCastException(errorId, message, innerException: null);
        }

        // Even though this never returns, expression trees expect non-void values in places, and so it's easier
        // to claim it returns object than to add extra expressions to keep the trees type safe.
        internal static object ThrowInvalidConversionException(object valueToConvert, Type resultType)
        {
            typeConversion.WriteLine("Issuing an error message about not being able to convert to non-core type.");
            throw new PSInvalidCastException(
                "ConversionSupportedOnlyToCoreTypes",
                innerException: null,
                ExtendedTypeSystem.InvalidCastExceptionNonCoreType,
                resultType.ToString());
        }

        private static IConversionData FigureLanguageConversion(
            Type fromType,
            Type toType,
            out PSConverter<object> valueDependentConversion,
            out ConversionRank valueDependentRank)
        {
            valueDependentConversion = null;
            valueDependentRank = ConversionRank.None;

            Type underlyingType = Nullable.GetUnderlyingType(toType);
            if (underlyingType != null)
            {
                IConversionData nullableConversion = FigureConversion(fromType, underlyingType);
                if (nullableConversion.Rank != ConversionRank.None)
                {
                    return CacheConversion<object>(fromType, toType, ConvertToNullable, nullableConversion.Rank);
                }
            }

            if (toType == typeof(void))
            {
                return CacheConversion<object>(fromType, toType, ConvertToVoid, ConversionRank.Language);
            }

            if (toType == typeof(bool))
            {
                PSConverter<bool> converter;
                if (typeof(IList).IsAssignableFrom(fromType))
                {
                    converter = ConvertIListToBool;
                }
                else if (fromType.IsEnum)
                {
                    converter = CreateNumericToBoolConverter(fromType);
                }
                else if (fromType.IsValueType)
                {
                    converter = ConvertValueToBool;
                }
                else
                {
                    converter = ConvertClassToBool;
                }

                return CacheConversion<bool>(fromType, toType, converter, ConversionRank.Language);
            }

            if (toType == typeof(string))
            {
                Diagnostics.Assert(
                    !IsNumeric(GetTypeCode(fromType)) || fromType.IsEnum,
                    "Number to string should be cached on initialization of cache table");

                return CacheConversion<string>(fromType, toType, ConvertNonNumericToString, ConversionRank.ToString);
            }

            if (toType.IsArray)
            {
                Type toElementType = toType.GetElementType();

                if (fromType.IsArray)
                {
                    if (toElementType.IsAssignableFrom(fromType.GetElementType()))
                    {
                        return CacheConversion<object>(fromType, toType, ConvertRelatedArrays, ConversionRank.Language);
                    }

                    return CacheConversion<object>(
                        fromType,
                        toType,
                        ConvertUnrelatedArrays,
                        ConversionRank.UnrelatedArrays);
                }

                if (IsTypeEnumerable(fromType))
                {
                    return CacheConversion<object>(fromType, toType, ConvertEnumerableToArray, ConversionRank.Language);
                }

                IConversionData data = FigureConversion(fromType, toElementType);
                if (data.Rank != ConversionRank.None)
                {
                    valueDependentRank = data.Rank & ConversionRank.ValueDependent;
                    valueDependentConversion = ConvertScalarToArray;
                    return null;
                }
            }

            if (toType == typeof(Array))
            {
                if (fromType.IsArray || fromType == typeof(Array))
                {
                    return CacheConversion<object>(fromType, toType, ConvertAssignableFrom, ConversionRank.Assignable);
                }

                if (IsTypeEnumerable(fromType))
                {
                    return CacheConversion<object>(fromType, toType, ConvertEnumerableToArray, ConversionRank.Language);
                }

                valueDependentRank = ConversionRank.Assignable & ConversionRank.ValueDependent;
                valueDependentConversion = ConvertScalarToArray;
                return null;
            }

            if (toType == typeof(Hashtable))
            {
                if (typeof(IDictionary).IsAssignableFrom(fromType))
                {
                    return CacheConversion<Hashtable>(
                        fromType,
                        toType,
                        ConvertIDictionaryToHashtable,
                        ConversionRank.Language);
                }
                else
                {
                    return null;
                }
            }

            if (toType == typeof(PSReference))
            {
                return CacheConversion<PSReference>(fromType, toType, ConvertToPSReference, ConversionRank.Language);
            }

            if (toType == typeof(XmlDocument))
            {
                return CacheConversion<XmlDocument>(fromType, toType, ConvertToXml, ConversionRank.Language);
            }

            if (toType == typeof(StringCollection))
            {
                ConversionRank rank = fromType.IsArray || IsTypeEnumerable(fromType)
                    ? ConversionRank.Language
                    : ConversionRank.LanguageS2A;
                return CacheConversion<StringCollection>(fromType, toType, ConvertToStringCollection, rank);
            }

            if (toType.IsSubclassOf(typeof(Delegate))
                && (fromType == typeof(ScriptBlock) || fromType.IsSubclassOf(typeof(ScriptBlock))))
            {
                return CacheConversion<Delegate>(fromType, toType, ConvertScriptBlockToDelegate, ConversionRank.Language);
            }

            if (toType == typeof(InternalPSCustomObject))
            {
                Type actualResultType = typeof(PSObject);

                ConstructorInfo resultConstructor = actualResultType.GetConstructor(Type.EmptyTypes);

                var converterObj = new ConvertViaNoArgumentConstructor(resultConstructor, actualResultType);
                return CacheConversion(fromType, toType, converterObj.Convert, ConversionRank.Language);
            }

            TypeCode fromTypeCode = GetTypeCode(fromType);
            if (IsInteger(fromTypeCode) && toType.IsEnum)
            {
                return CacheConversion<object>(fromType, toType, ConvertIntegerToEnum, ConversionRank.Language);
            }

            if (fromType.IsSubclassOf(typeof(PSMethod))
                && toType.IsSubclassOf(typeof(Delegate))
                && !toType.IsAbstract)
            {
                MethodInfo targetMethod = toType.GetMethod("Invoke");
                var comparator = new SignatureComparator(targetMethod);
                var signatureEnumerator = new PSMethodSignatureEnumerator(fromType);
                int index = -1, matchedIndex = -1;

                while (signatureEnumerator.MoveNext())
                {
                    index++;
                    Type signatureType = signatureEnumerator.Current;

                    // Skip the non-bindable signatures
                    if (signatureType == typeof(Func<PSNonBindableType>))
                    {
                        continue;
                    }

                    Type[] argumentTypes = signatureType.GenericTypeArguments;
                    if (comparator.ProjectedSignatureMatchesTarget(argumentTypes, out bool signaturesMatchExactly))
                    {
                        if (signaturesMatchExactly)
                        {
                            // We prefer the signature that exactly matches the target delegate.
                            matchedIndex = index;
                            break;
                        }

                        // If there is no exact match, then we use the first compatible signature we found.
                        if (matchedIndex == -1)
                        {
                            matchedIndex = index;
                        }
                    }
                }

                if (matchedIndex > -1)
                {
                    // We got the index of the matching method signature based on the PSMethod<..> type.
                    // Signatures in PSMethod<..> type were constructed based on the array of method overloads,
                    // in the exact order. So we can use this index directly to locate the matching overload in
                    // the converter, without having to compare the signature again.
                    var converter = PSMethodToDelegateConverter.GetConverter(matchedIndex);
                    return CacheConversion<Delegate>(fromType, toType, converter.Convert, ConversionRank.Language);
                }
            }

            return null;
        }

        private struct SignatureComparator
        {
            enum TypeMatchingContext
            {
                ReturnType,
                ParameterType,
                OutParameterType
            }

            private readonly ParameterInfo[] targetParameters;
            private readonly Type targetReturnType;

            internal SignatureComparator(MethodInfo targetMethodInfo)
            {
                targetReturnType = targetMethodInfo.ReturnType;
                targetParameters = targetMethodInfo.GetParameters();
            }

            /// <summary>
            /// Check if a projected signature matches the target method.
            /// </summary>
            /// <param name="argumentTypes">
            /// The type arguments from the metadata type 'Func[..]' that represents the projected signature.
            /// It contains the return type as the last item in the array.
            /// </param>
            /// <param name="signaturesMatchExactly">
            /// Set by this method to indicate if it's an exact match.
            /// </param>
            internal bool ProjectedSignatureMatchesTarget(Type[] argumentTypes, out bool signaturesMatchExactly)
            {
                signaturesMatchExactly = false;
                int length = argumentTypes.Length;
                if (length != targetParameters.Length + 1)
                {
                    return false;
                }

                bool allTypesMatchExactly;
                Type sourceReturnType = argumentTypes[length - 1];

                if (ProjectedTypeMatchesTargetType(
                    sourceReturnType, targetReturnType,
                    TypeMatchingContext.ReturnType,
                    out bool typesMatchExactly))
                {
                    allTypesMatchExactly = typesMatchExactly;
                    for (int i = 0; i < targetParameters.Length; i++)
                    {
                        ParameterInfo targetParam = targetParameters[i];
                        Type sourceType = argumentTypes[i];
                        TypeMatchingContext matchContext = targetParam.IsOut
                            ? TypeMatchingContext.OutParameterType
                            : TypeMatchingContext.ParameterType;

                        if (!ProjectedTypeMatchesTargetType(
                            sourceType,
                            targetParam.ParameterType,
                            matchContext,
                            out typesMatchExactly))
                        {
                            return false;
                        }

                        allTypesMatchExactly &= typesMatchExactly;
                    }

                    signaturesMatchExactly = allTypesMatchExactly;
                    return true;
                }

                return false;
            }

            private static bool ProjectedTypeMatchesTargetType(
                Type sourceType,
                Type targetType,
                TypeMatchingContext matchContext,
                out bool matchExactly)
            {
                matchExactly = false;
                if (targetType.IsByRef || targetType.IsPointer)
                {
                    if (!sourceType.IsGenericType)
                    {
                        return false;
                    }

                    var sourceTypeDef = sourceType.GetGenericTypeDefinition();
                    bool isOutParameter = matchContext == TypeMatchingContext.OutParameterType;

                    if (targetType.IsByRef && sourceTypeDef == (isOutParameter ? typeof(PSOutParameter<>) : typeof(PSReference<>))
                        || targetType.IsPointer && sourceTypeDef == typeof(PSPointer<>))
                    {
                        // For ref/out parameter types and pointer types, the element types need to match exactly.
                        if (targetType.GetElementType() == sourceType.GenericTypeArguments[0])
                        {
                            matchExactly = true;
                            return true;
                        }
                    }

                    return false;
                }

                if (targetType == sourceType
                    || targetType == typeof(void) && sourceType == typeof(VOID)
                    || targetType == typeof(TypedReference) && sourceType == typeof(PSTypedReference))
                {
                    matchExactly = true;
                    return true;
                }

                if (targetType == typeof(void) || targetType == typeof(TypedReference))
                {
                    return false;
                }

                return matchContext == TypeMatchingContext.ReturnType
                    ? targetType.IsAssignableFrom(sourceType)
                    : sourceType.IsAssignableFrom(targetType);
            }
        }

        private static PSConverter<object> FigureStaticCreateMethodConversion(Type fromType, Type toType)
        {
            // after discussing this with Jason, we decided that for now we only want to support string->CimSession conversion
            // and we don't want to add a Parse-like conversion based on a static Create method

            if (fromType == typeof(string) && toType == typeof(Microsoft.Management.Infrastructure.CimSession))
            {
                return ConvertStringToCimSession;
            }

            return null;
        }

        private static PSConverter<object> FigureParseConversion(Type fromType, Type toType)
        {
            if (toType.IsEnum)
            {
                if (fromType == typeof(string))
                {
                    return ConvertStringToEnum;
                }

                if (IsTypeEnumerable(fromType))
                {
                    return ConvertEnumerableToEnum;
                }
            }
            else if (fromType == typeof(string))
            {
                const BindingFlags parseFlags = BindingFlags.FlattenHierarchy
                    | BindingFlags.Public
                    | BindingFlags.Static;

                // GetMethod could throw for more than one match, for instance
                MethodInfo parse = null;
                try
                {
                    parse = toType.GetMethod(
                        name: "Parse",
                        bindingAttr: parseFlags,
                        binder: null,
                        types: new Type[2] { typeof(string), typeof(IFormatProvider) },
                        modifiers: null);
                }
                catch (AmbiguousMatchException e)
                {
                    typeConversion.WriteLine("Exception finding Parse method with CultureInfo: \"{0}\".", e.Message);
                }
                catch (ArgumentException e)
                {
                    typeConversion.WriteLine("Exception finding Parse method with CultureInfo: \"{0}\".", e.Message);
                }

                if (parse != null)
                {
                    var converter = new ConvertViaParseMethod
                    {
                        parse = parse
                    };

                    return converter.ConvertWithCulture;
                }

                try
                {
                    parse = toType.GetMethod(
                        name: "Parse",
                        bindingAttr: parseFlags,
                        binder: null,
                        types: new Type[1] { typeof(string) },
                        modifiers: null);
                }
                catch (AmbiguousMatchException e)
                {
                    typeConversion.WriteLine("Exception finding Parse method: \"{0}\".", e.Message);
                }
                catch (ArgumentException e)
                {
                    typeConversion.WriteLine("Exception finding Parse method: \"{0}\".", e.Message);
                }

                if (parse != null)
                {
                    var converter = new ConvertViaParseMethod
                    {
                        parse = parse
                    };

                    return converter.ConvertWithoutCulture;
                }
            }

            return null;
        }

        /// <summary>
        /// Figure conversion when following conditions are satisfied:
        /// 1. toType is a closed generic type and it has a constructor that takes IEnumerable[T], ICollection[T] or IList[T]
        /// 2. fromType is System.Array, System.Object[] or it's the same as the element type of toType.
        /// </summary>
        /// <param name="fromType"></param>
        /// <param name="toType"></param>
        /// <returns></returns>
        internal static Tuple<PSConverter<object>, ConversionRank> FigureIEnumerableConstructorConversion(
            Type fromType,
            Type toType)
        {
            // Win8: 653180. If toType is an Abstract type then we cannot construct it anyway. So, bailing out fast.
            if (toType.IsAbstract == true)
            {
                return null;
            }

            try
            {
                bool result = false;
                bool isScalar = false;
                Type elementType = null;
                ConstructorInfo resultConstructor = null;

                if (toType.IsGenericType
                    && !toType.ContainsGenericParameters
                    && (typeof(IList).IsAssignableFrom(toType)
                        || typeof(ICollection).IsAssignableFrom(toType)
                        || typeof(IEnumerable).IsAssignableFrom(toType)))
                {
                    Type[] argTypes = toType.GetGenericArguments();
                    if (argTypes.Length != 1)
                    {
                        typeConversion.WriteLine(
                            "toType has more than one generic arguments. Here we only care about the toType which contains only one generic argument and whose constructor takes IEnumerable<T>, ICollection<T> or IList<T>.");
                        return null;
                    }

                    elementType = argTypes[0];

                    if (typeof(Array) == fromType
                        || typeof(object[]) == fromType
                        || elementType.IsAssignableFrom(fromType)
                        // WinBlue: 423899 : To support scenario like [list[int]]"4"
                        || FigureConversion(fromType, elementType) != null)
                    {
                        isScalar = elementType.IsAssignableFrom(fromType);
                        ConstructorInfo[] constructors = toType.GetConstructors();
                        Type iEnumerableClosedType = typeof(IEnumerable<>).MakeGenericType(elementType);
                        Type iCollectionClosedType = typeof(ICollection<>).MakeGenericType(elementType);
                        Type iListClosedType = typeof(IList<>).MakeGenericType(elementType);

                        foreach (ConstructorInfo ctor in constructors)
                        {
                            ParameterInfo[] param = ctor.GetParameters();
                            if (param.Length != 1)
                            {
                                continue;
                            }

                            Type paramType = param[0].ParameterType;
                            if (iEnumerableClosedType == paramType
                                || iCollectionClosedType == paramType
                                || iListClosedType == paramType)
                            {
                                resultConstructor = ctor;
                                result = true;
                                break;
                            }
                        }
                    }
                }

                if (result)
                {
                    var converter = new ConvertViaIEnumerableConstructor();

                    try
                    {
                        Type listClosedType = typeof(List<>).MakeGenericType(elementType);
                        ConstructorInfo listCtor = listClosedType.GetConstructor(new Type[] { typeof(int) });

                        converter.ListCtorLambda = CreateCtorLambdaClosure<int, IList>(
                            listCtor,
                            typeof(int),
                            useExplicitConversion: false);

                        ParameterInfo[] targetParams = resultConstructor.GetParameters();
                        Type targetParamType = targetParams[0].ParameterType;

                        converter.TargetCtorLambda = CreateCtorLambdaClosure<IList, object>(
                            resultConstructor,
                            targetParamType,
                            useExplicitConversion: false);

                        converter.ElementType = elementType;
                        converter.IsScalar = isScalar;
                    }
                    catch (Exception e)
                    {
                        typeConversion.WriteLine("Exception building constructor lambda: \"{0}\"", e.Message);
                        return null;
                    }

                    ConversionRank rank = isScalar ? ConversionRank.ConstructorS2A : ConversionRank.Constructor;
                    typeConversion.WriteLine("Conversion is figured out. Conversion rank: \"{0}\"", rank);

                    return new Tuple<PSConverter<object>, ConversionRank>(converter.Convert, rank);
                }
                else
                {
                    typeConversion.WriteLine(
                        "Failed to figure out the conversion from \"{0}\" to \"{1}\"",
                        fromType.FullName,
                        toType.FullName);

                    return null;
                }
            }
            catch (ArgumentException ae)
            {
                typeConversion.WriteLine("Exception finding IEnumerable conversion: \"{0}\".", ae.Message);
            }
            catch (InvalidOperationException ie)
            {
                typeConversion.WriteLine("Exception finding IEnumerable conversion: \"{0}\".", ie.Message);
            }
            catch (NotSupportedException ne)
            {
                typeConversion.WriteLine("Exception finding IEnumerable conversion: \"{0}\".", ne.Message);
            }

            return null;
        }

        private static Func<T1, T2> CreateCtorLambdaClosure<T1, T2>(
            ConstructorInfo ctor,
            Type realParamType,
            bool useExplicitConversion)
        {
            ParameterExpression paramExpr = Expression.Parameter(typeof(T1), "args");
            Expression castParamExpr = useExplicitConversion
                ? (Expression)Expression.Call(
                    CachedReflectionInfo.Convert_ChangeType,
                    paramExpr,
                    Expression.Constant(realParamType, typeof(Type)))
                : Expression.Convert(paramExpr, realParamType);

            NewExpression ctorExpr = Expression.New(ctor, castParamExpr.Cast(realParamType));
            return Expression.Lambda<Func<T1, T2>>(body: ctorExpr.Cast(typeof(T2)), paramExpr).Compile();
        }

        internal static PSConverter<object> FigureConstructorConversion(Type fromType, Type toType)
        {
            if (IsIntegralType(fromType) && (typeof(IList).IsAssignableFrom(toType)
                || typeof(ICollection).IsAssignableFrom(toType)))
            {
                typeConversion.WriteLine(
                    "Ignoring the collection constructor that takes an integer, since this is not semantically a conversion.");

                return null;
            }

            ConstructorInfo resultConstructor = null;
            try
            {
                resultConstructor = toType.GetConstructor(new Type[] { fromType });
            }
            catch (AmbiguousMatchException e)
            {
                typeConversion.WriteLine("Exception finding Constructor: \"{0}\".", e.Message);
            }
            catch (ArgumentException e)
            {
                typeConversion.WriteLine("Exception finding Constructor: \"{0}\".", e.Message);
            }

            if (resultConstructor == null)
            {
                return null;
            }

            typeConversion.WriteLine("Found Constructor.");
            var converter = new ConvertViaConstructor();

            try
            {
                ParameterInfo[] targetParams = resultConstructor.GetParameters();
                Type targetParamType = targetParams[0].ParameterType;
                bool useExplicitConversion = targetParamType.IsValueType
                    && fromType != targetParamType
                    && Nullable.GetUnderlyingType(targetParamType) == null;

                converter.TargetCtorLambda = CreateCtorLambdaClosure<object, object>(
                    resultConstructor,
                    targetParamType,
                    useExplicitConversion);
            }
            catch (Exception e)
            {
                typeConversion.WriteLine("Exception building constructor lambda: \"{0}\"", e.Message);
                return null;
            }

            typeConversion.WriteLine("Conversion is figured out.");
            return converter.Convert;
        }

        private static bool IsIntegralType(Type type)
            => type == typeof(sbyte)
                || type == typeof(byte)
                || type == typeof(short)
                || type == typeof(ushort)
                || type == typeof(int)
                || type == typeof(uint)
                || type == typeof(long)
                || type == typeof(ulong);

        internal static PSConverter<object> FigurePropertyConversion(
            Type fromType,
            Type toType,
            ref ConversionRank rank)
        {
            if (!typeof(PSObject).IsAssignableFrom(fromType) || toType.IsAbstract)
            {
                return null;
            }

            ConstructorInfo toConstructor = null;
            try
            {
                toConstructor = toType.GetConstructor(Type.EmptyTypes);
            }
            catch (AmbiguousMatchException e)
            {
                typeConversion.WriteLine("Exception finding Constructor: \"{0}\".", e.Message);
            }
            catch (ArgumentException e)
            {
                typeConversion.WriteLine("Exception finding Constructor: \"{0}\".", e.Message);
            }

            if (toConstructor == null && !toType.IsValueType)
            {
                return null;
            }

            if (toType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length == 0
                && toType.GetFields(BindingFlags.Public | BindingFlags.Instance).Length == 0)
            {
                // fromType is PSObject, toType has no properties/fields to set, so conversion should fail.
                return null;
            }

            typeConversion.WriteLine("Found Constructor.");

            try
            {
                var noArgumentConstructorConverter = new ConvertViaNoArgumentConstructor(toConstructor, toType);
                rank = ConversionRank.Constructor;

                return noArgumentConstructorConverter.Convert;
            }
            catch (ArgumentException ae)
            {
                typeConversion.WriteLine("Exception converting via no argument constructor: \"{0}\".", ae.Message);
            }
            catch (InvalidOperationException ie)
            {
                typeConversion.WriteLine("Exception converting via no argument constructor: \"{0}\".", ie.Message);
            }

            rank = ConversionRank.None;
            return null;
        }

        internal static PSConverter<object> FigureCastConversion(Type fromType, Type toType, ref ConversionRank rank)
        {
            MethodInfo castOperator = FindCastOperator("op_Implicit", toType, fromType, toType);

            if (castOperator == null)
            {
                castOperator = FindCastOperator("op_Explicit", toType, fromType, toType);

                if (castOperator == null)
                {
                    castOperator = FindCastOperator("op_Implicit", fromType, fromType, toType)
                        ?? FindCastOperator("op_Explicit", fromType, fromType, toType);
                }
            }

            if (castOperator != null)
            {
                rank = castOperator.Name.Equals("op_Implicit", StringComparison.OrdinalIgnoreCase)
                    ? ConversionRank.ImplicitCast
                    : ConversionRank.ExplicitCast;

                var converter = new ConvertViaCast
                {
                    cast = castOperator
                };

                return converter.Convert;
            }

            return null;
        }

        private static bool TypeConverterPossiblyExists(Type type)
        {
            lock (s_possibleTypeConverter)
            {
                if (s_possibleTypeConverter.ContainsKey(type.FullName))
                {
                    return true;
                }
            }

            // GetCustomAttributes returns IEnumerable<Attribute> in CoreCLR
            return type.GetCustomAttributes(typeof(TypeConverterAttribute), inherit: false).Any();
        }

        private static readonly Dictionary<string, bool> s_possibleTypeConverter = new Dictionary<string, bool>(16);

        // This is the internal dummy type used when an IDictionary is converted to a pscustomobject
        // PS C:\> $ps = [pscustomobject]@{a=10;b=5}
        // PS C:\> $ps = [pscustomobject][ordered]@{a=10;b=5}
        // Whenever we see a conversion to PSCustomObject, we represent it as a conversion to InternalPSCustomObject
        // This is introduced to avoid breaking PSObject behavior.
        // (Because PSCustomObject is a typeaccelerator for PSObject, we needed a separate type to represent type conversions to PSCustomObject)
        internal class InternalPSCustomObject
        {
        }

        internal class InternalPSObject : PSObject
        {
        }

        internal static IConversionData FigureConversion(Type fromType, Type toType)
        {
            IConversionData data = GetConversionData(fromType, toType);
            if (data != null)
            {
                return data;
            }

            if (fromType == typeof(Null))
            {
                return FigureConversionFromNull(toType);
            }

            if (toType.IsAssignableFrom(fromType))
            {
                return CacheConversion<object>(
                    fromType,
                    toType,
                    ConvertAssignableFrom,
                    toType == fromType ? ConversionRank.Identity : ConversionRank.Assignable);
            }

            if (fromType.IsByRefLike || toType.IsByRefLike)
            {
                // ByRef-like types are not boxable and should be used on stack only.
                return CacheConversion(fromType, toType, ConvertNoConversion, ConversionRank.None);
            }

            if (typeof(PSObject).IsAssignableFrom(fromType) && typeof(InternalPSObject) != fromType)
            {
                // We don't attempt converting PSObject (or derived) to anything else,
                // instead we go straight to PSObject.Base (which is only a PSObject
                // when no object is wrapped, in which case we try conversions from object)
                // and convert that instead.
                return CacheConversion(fromType, toType, ConvertNoConversion, ConversionRank.None);
            }

            if (toType == typeof(PSObject))
            {
                return CacheConversion<PSObject>(fromType, toType, ConvertToPSObject, ConversionRank.PSObject);
            }

            PSConverter<object> converter = null;
            ConversionRank rank = ConversionRank.None;

            // If we've ever used ConstrainedLanguage, check if the target type is allowed
            if (ExecutionContext.HasEverUsedConstrainedLanguage)
            {
                ExecutionContext context = LocalPipeline.GetExecutionContextFromTLS();

                if (context != null && context.LanguageMode == PSLanguageMode.ConstrainedLanguage)
                {
                    if (toType != typeof(object)
                        && toType != typeof(object[])
                        && !CoreTypes.Contains(toType))
                    {
                        converter = ConvertNotSupportedConversion;
                        rank = ConversionRank.None;
                        return CacheConversion(fromType, toType, converter, rank);
                    }
                }
            }

            // Assemblies in CoreCLR might not allow reflection execution on their internal types.
            if (!TypeResolver.IsPublic(toType) && DotNetAdapter.DisallowPrivateReflection(toType))
            {
                // If the type is non-public and reflection execution is not allowed on it, then we return
                // 'ConvertNoConversion', because we won't be able to invoke constructor, methods or set
                // properties on an instance of this type through reflection.
                return CacheConversion(fromType, toType, ConvertNoConversion, ConversionRank.None);
            }

            ConversionRank valueDependentRank = ConversionRank.None;
            IConversionData conversionData = FigureLanguageConversion(
                fromType,
                toType,
                out PSConverter<object> valueDependentConversion,
                out valueDependentRank);

            if (conversionData != null)
            {
                return conversionData;
            }

            rank = valueDependentConversion != null ? ConversionRank.Language : ConversionRank.None;
            converter = FigureParseConversion(fromType, toType);
            if (converter == null)
            {
                converter = FigureStaticCreateMethodConversion(fromType, toType);
                if (converter == null)
                {
                    converter = FigureConstructorConversion(fromType, toType);
                    rank = ConversionRank.Constructor;
                    if (converter == null)
                    {
                        converter = FigureCastConversion(fromType, toType, ref rank);
                        if (converter == null)
                        {
                            if (typeof(IConvertible).IsAssignableFrom(fromType))
                            {
                                if (IsNumeric(GetTypeCode(fromType)) && !fromType.IsEnum)
                                {
                                    if (!toType.IsArray)
                                    {
                                        if (GetConversionRank(typeof(string), toType) != ConversionRank.None)
                                        {
                                            converter = ConvertNumericIConvertible;
                                            rank = ConversionRank.IConvertible;
                                        }
                                    }
                                }
                                else if (fromType != typeof(string))
                                {
                                    converter = ConvertIConvertible;
                                    rank = ConversionRank.IConvertible;
                                }
                            }
                            else if (typeof(IDictionary).IsAssignableFrom(fromType))
                            {
                                // We need to call the null argument constructor only if the following 2 conditions satisfy
                                //  1) if the fromType is either a hashtable or OrderedDictionary
                                //  2) if the ToType does not already have a constructor that takes a hashtable or OrderedDictionary. (This is to avoid breaking existing apps.)
                                // If the ToType has a constructor that takes a hashtable or OrderedDictionary,
                                // then it would have been returned as the constructor during FigureConstructorConversion
                                // So, we need to check only for the first condition
                                ConstructorInfo resultConstructor = toType.GetConstructor(Type.EmptyTypes);

                                if (resultConstructor != null || (toType.IsValueType && !toType.IsPrimitive))
                                {
                                    var noArgumentConstructorConverter =
                                        new ConvertViaNoArgumentConstructor(resultConstructor, toType);
                                    converter = noArgumentConstructorConverter.Convert;
                                    rank = ConversionRank.Constructor;
                                }
                            }
                        }
                    }
                    else
                    {
                        rank = ConversionRank.Constructor;
                    }
                }
                else
                {
                    rank = ConversionRank.Create;
                }
            }
            else
            {
                rank = ConversionRank.Parse;
            }

            if (converter == null)
            {
                Tuple<PSConverter<object>, ConversionRank> tuple = FigureIEnumerableConstructorConversion(fromType, toType);
                if (tuple != null)
                {
                    (converter, rank) = tuple;
                }
            }

            if (converter == null)
            {
                converter = FigurePropertyConversion(fromType, toType, ref rank);
            }

            if (TypeConverterPossiblyExists(fromType)
                || TypeConverterPossiblyExists(toType)
                || converter != null && valueDependentConversion != null)
            {
                var customConverter = new ConvertCheckingForCustomConverter
                {
                    tryfirstConverter = valueDependentConversion,
                    fallbackConverter = converter
                };

                converter = customConverter.Convert;
                if (valueDependentRank > rank)
                {
                    rank = valueDependentRank;
                }
                else if (rank == ConversionRank.None)
                {
                    rank = ConversionRank.Custom;
                }
            }
            else if (valueDependentConversion != null)
            {
                converter = valueDependentConversion;
                rank = valueDependentRank;
            }

            if (converter == null)
            {
                converter = ConvertNoConversion;
                rank = ConversionRank.None;
            }

            return CacheConversion(fromType, toType, converter, rank);
        }

        internal class Null
        {
        }

        private static IConversionData FigureConversionFromNull(Type toType)
        {
            IConversionData data = GetConversionData(typeof(Null), toType);
            if (data != null)
            {
                return data;
            }

            if (Nullable.GetUnderlyingType(toType) != null)
            {
                return CacheConversion<object>(typeof(Null), toType, ConvertNullToNullable, ConversionRank.NullToValue);
            }
            else if (!toType.IsValueType)
            {
                return CacheConversion<object>(typeof(Null), toType, ConvertNullToRef, ConversionRank.NullToRef);
            }

            return CacheConversion(typeof(Null), toType, ConvertNoConversion, ConversionRank.None);
        }

        internal static string ObjectToTypeNameString(object obj)
        {
            if (obj == null)
            {
                return "null";
            }

            PSObject pso = PSObject.AsPSObject(obj);
            ConsolidatedString typeNames = pso.InternalTypeNames;

            if (typeNames != null && typeNames.Count > 0)
            {
                return typeNames[0];
            }

            return Microsoft.PowerShell.ToStringCodeMethods.Type(obj.GetType());
        }

        #endregion type converter
    }
}

#pragma warning restore 56500
