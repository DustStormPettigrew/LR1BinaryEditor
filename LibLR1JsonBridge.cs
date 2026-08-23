using LibLR1.Schema;
using LibLR1.Inspection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LR1BinaryEditor
{
	internal static class LibLR1JsonBridge
	{
		private const string DocumentSchema = "lr1binaryeditor.liblr1-json.v1";

		private static readonly Assembly s_libAssembly = typeof(LibLR1.RAB).Assembly;
		private static readonly Dictionary<string, FormatAdapter> s_adapters =
			new Dictionary<string, FormatAdapter>(StringComparer.OrdinalIgnoreCase);
		private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
		{
			WriteIndented = true
		};

		public static bool CanExport(string format, out string error)
		{
			return TryGetAdapter(format, out _, out error);
		}

		public static bool CanImport(string format, out string error)
		{
			return TryGetAdapter(format, out _, out error);
		}

		public static bool TryExportJson(string format, string fileName, object model, byte[] sourceData, out string json, out string error)
		{
			json = null;
			if (!TryGetAdapter(format, out FormatAdapter adapter, out error))
			{
				return false;
			}

			if (model == null || !adapter.ModelType.IsInstanceOfType(model))
			{
				error = "JSON export requires a validated LibLR1 " + adapter.ModelType.Name + " document.";
				return false;
			}

			try
			{
				Dictionary<string, object> document = new Dictionary<string, object>
				{
					["schema"] = DocumentSchema,
					["format"] = adapter.Format,
					["fileName"] = fileName,
					["rootType"] = adapter.ModelType.FullName,
					["sourceData"] = Convert.ToBase64String(sourceData ?? Array.Empty<byte>()),
					["data"] = SerializeValue(model)
				};
				json = JsonSerializer.Serialize(document, s_jsonOptions);
				error = null;
				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		public static bool TryImportJson(string json, out ImportedJsonDocument document, out string error)
		{
			document = null;
			error = null;

			try
			{
				using JsonDocument parsed = JsonDocument.Parse(json);
				JsonElement root = parsed.RootElement;

				if (!root.TryGetProperty("format", out JsonElement formatElement))
				{
					error = "The JSON file is missing a `format` property.";
					return false;
				}

				string format = formatElement.GetString();
				if (!TryGetAdapter(format, out FormatAdapter adapter, out error))
				{
					return false;
				}

				string rootTypeName = root.TryGetProperty("rootType", out JsonElement rootTypeElement)
					? rootTypeElement.GetString()
					: adapter.ModelType.FullName;
				Type rootType = ResolveType(rootTypeName);
				if (rootType == null)
				{
					error = "Unable to resolve LibLR1 type `" + rootTypeName + "`.";
					return false;
				}

				if (!adapter.ModelType.IsAssignableFrom(rootType))
				{
					error = "JSON root type `" + rootType.FullName + "` does not match format ." + adapter.Format + ".";
					return false;
				}

				if (!root.TryGetProperty("data", out JsonElement dataElement))
				{
					error = "The JSON file is missing a `data` property.";
					return false;
				}

				if (!root.TryGetProperty("sourceData", out JsonElement sourceElement))
				{
					error = "The JSON file is missing the validated `sourceData` baseline required for safe import.";
					return false;
				}
				object model = ReadValidatedBaseline(adapter.Format, Convert.FromBase64String(sourceElement.GetString()));
				PopulateObject(dataElement, model, rootType);
				document = new ImportedJsonDocument
				{
					Format = adapter.Format,
					FileName = root.TryGetProperty("fileName", out JsonElement fileNameElement)
						? fileNameElement.GetString()
						: "Imported." + adapter.Format,
					Model = model
				};
				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		private static bool TryGetAdapter(string format, out FormatAdapter adapter, out string error)
		{
			adapter = null;
			error = null;
			if (string.IsNullOrWhiteSpace(format))
			{
				error = "No file format is selected.";
				return false;
			}

			if (s_adapters.TryGetValue(format, out adapter))
			{
				return true;
			}

			string normalized = format.Trim().TrimStart('.').ToUpperInvariant();
			if (!SchemaStructureProvider.Formats.Contains(normalized, StringComparer.OrdinalIgnoreCase))
			{
				error = "No registered LibLR1 format was found for ." + format + " files.";
				return false;
			}

			adapter = new FormatAdapter(normalized, SchemaStructureProvider.GetRootType(normalized));
			s_adapters[adapter.Format] = adapter;
			return true;
		}

		private static object SerializeValue(object value)
		{
			if (value == null)
			{
				return null;
			}

			Type type = value.GetType();
			if (IsSimpleType(type))
			{
				return value;
			}

			// byte[] → base64 string for compact round-trip representation
			if (type == typeof(byte[]))
			{
				return Convert.ToBase64String((byte[])value);
			}

			if (TryGetDictionaryTypes(type, out _, out _))
			{
				Dictionary<string, object> output = new Dictionary<string, object>(StringComparer.Ordinal);
				foreach (SerializedDictionaryEntry entry in EnumerateDictionaryEntries((IEnumerable)value)
					.OrderBy(item => item.KeyString, StringComparer.Ordinal))
				{
					output[entry.KeyString] = SerializeValue(entry.Value);
				}
				return output;
			}

			if (IsEnumerableType(type))
			{
				List<object> output = new List<object>();
				foreach (object item in (IEnumerable)value)
				{
					output.Add(SerializeValue(item));
				}
				return output;
			}

			Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal)
			{
				["$type"] = type.FullName
			};

			foreach (SerializableMember member in GetSerializableMembers(type))
			{
				result[member.Name] = SerializeValue(member.GetValue(value));
			}

			return result;
		}

		private static object DeserializeValue(JsonElement element, Type targetType)
		{
			if (element.ValueKind == JsonValueKind.Null)
			{
				return null;
			}

			Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
			if (IsSimpleType(underlyingType))
			{
				return DeserializeSimpleValue(element, underlyingType);
			}

			if (TryGetDictionaryTypes(underlyingType, out Type keyType, out Type valueType))
			{
				object dictionary = CreateDictionaryInstance(underlyingType, keyType, valueType);
				MethodInfo addMethod = dictionary.GetType().GetMethod("Add", new[] { keyType, valueType });
				foreach (JsonProperty property in element.EnumerateObject())
				{
					object key = ConvertKey(property.Name, keyType);
					object value = DeserializeValue(property.Value, valueType);
					addMethod.Invoke(dictionary, new[] { key, value });
				}
				return dictionary;
			}

			// byte[] is deserialized from base64 string (matches SerializeValue encoding)
			if (underlyingType == typeof(byte[]))
			{
				return Convert.FromBase64String(element.GetString());
			}

			if (underlyingType.IsArray)
			{
				Type elementType = underlyingType.GetElementType();
				JsonElement.ArrayEnumerator items = element.EnumerateArray();
				List<object> values = new List<object>();
				foreach (JsonElement item in items)
				{
					values.Add(DeserializeValue(item, elementType));
				}

				Array array = Array.CreateInstance(elementType, values.Count);
				for (int i = 0; i < values.Count; i++)
				{
					array.SetValue(values[i], i);
				}
				return array;
			}

			if (TryGetListElementType(underlyingType, out Type listElementType))
			{
				object list = CreateListInstance(underlyingType, listElementType);
				MethodInfo addMethod = list.GetType().GetMethod("Add", new[] { listElementType });
				foreach (JsonElement item in element.EnumerateArray())
				{
					addMethod.Invoke(list, new[] { DeserializeValue(item, listElementType) });
				}
				return list;
			}

			Type actualType = ResolveActualType(element, underlyingType);
			object instance = CreateObject(actualType);
			Dictionary<string, SerializableMember> members = GetSerializableMembers(actualType)
				.ToDictionary(member => member.Name, member => member, StringComparer.OrdinalIgnoreCase);

			foreach (JsonProperty property in element.EnumerateObject())
			{
				if (property.NameEquals("$type"))
				{
					continue;
				}

				if (!members.TryGetValue(property.Name, out SerializableMember member))
				{
					continue;
				}

				member.SetValue(instance, DeserializeValue(property.Value, member.MemberType));
			}

			return instance;
		}

		private static object ReadValidatedBaseline(string p_format, byte[] p_sourceData)
		{
			string directory = Path.Combine(Path.GetTempPath(), "LR1BinaryEditor", "json");
			Directory.CreateDirectory(directory);
			string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + "." + p_format);
			try
			{
				File.WriteAllBytes(path, p_sourceData);
				FormatInspectionResult<object> inspection = FormatInspection.ReadRegistered(p_format, path);
				if (!inspection.CanWrite) throw new InvalidDataException(inspection.Issue?.Message ?? "The JSON source baseline is not writable.");
				return inspection.Document;
			}
			finally { try { File.Delete(path); } catch { } }
		}

		private static void PopulateObject(JsonElement p_element, object p_instance, Type p_type)
		{
			Dictionary<string, SerializableMember> members = GetSerializableMembers(p_type)
				.ToDictionary(member => member.Name, member => member, StringComparer.OrdinalIgnoreCase);
			foreach (JsonProperty property in p_element.EnumerateObject())
			{
				if (property.NameEquals("$type") || !members.TryGetValue(property.Name, out SerializableMember member)) continue;
				member.SetValue(p_instance, DeserializeValue(property.Value, member.MemberType));
			}
		}

		private static Type ResolveActualType(JsonElement element, Type fallbackType)
		{
			if (element.ValueKind == JsonValueKind.Object &&
				element.TryGetProperty("$type", out JsonElement typeElement))
			{
				string typeName = typeElement.GetString();
				Type resolvedType = ResolveType(typeName);
				if (resolvedType != null && fallbackType.IsAssignableFrom(resolvedType))
				{
					return resolvedType;
				}
			}

			return fallbackType;
		}

		private static Type ResolveType(string typeName)
		{
			if (string.IsNullOrWhiteSpace(typeName))
			{
				return null;
			}

			return s_libAssembly.GetType(typeName, false, true);
		}

		private static object DeserializeSimpleValue(JsonElement element, Type targetType)
		{
			if (targetType == typeof(string))
			{
				return element.GetString();
			}

			if (targetType == typeof(bool))
			{
				return element.GetBoolean();
			}

			if (targetType == typeof(byte))
			{
				return element.GetByte();
			}

			if (targetType == typeof(sbyte))
			{
				return element.GetSByte();
			}

			if (targetType == typeof(short))
			{
				return element.GetInt16();
			}

			if (targetType == typeof(ushort))
			{
				return element.GetUInt16();
			}

			if (targetType == typeof(int))
			{
				return element.GetInt32();
			}

			if (targetType == typeof(uint))
			{
				return element.GetUInt32();
			}

			if (targetType == typeof(long))
			{
				return element.GetInt64();
			}

			if (targetType == typeof(ulong))
			{
				return element.GetUInt64();
			}

			if (targetType == typeof(float))
			{
				return element.GetSingle();
			}

			if (targetType == typeof(double))
			{
				return element.GetDouble();
			}

			if (targetType == typeof(decimal))
			{
				return element.GetDecimal();
			}

			if (targetType == typeof(char))
			{
				string value = element.GetString();
				return string.IsNullOrEmpty(value) ? '\0' : value[0];
			}

			if (targetType.IsEnum)
			{
				return Enum.Parse(targetType, element.GetString(), true);
			}

			throw new NotSupportedException("Unsupported JSON primitive type `" + targetType.FullName + "`.");
		}

		private static object CreateObject(Type type)
		{
			if (type.IsValueType)
			{
				return Activator.CreateInstance(type);
			}

			ConstructorInfo constructor = type.GetConstructor(Type.EmptyTypes);
			if (constructor != null)
			{
				return constructor.Invoke(Array.Empty<object>());
			}

			return RuntimeHelpers.GetUninitializedObject(type);
		}

		private static object CreateListInstance(Type listType, Type elementType)
		{
			if (!listType.IsInterface && !listType.IsAbstract)
			{
				return CreateObject(listType);
			}

			return Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
		}

		private static object CreateDictionaryInstance(Type dictionaryType, Type keyType, Type valueType)
		{
			if (!dictionaryType.IsInterface && !dictionaryType.IsAbstract)
			{
				return CreateObject(dictionaryType);
			}

			return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType));
		}

		private static object ConvertKey(string key, Type keyType)
		{
			if (keyType == typeof(string))
			{
				return key;
			}

			Type underlyingType = Nullable.GetUnderlyingType(keyType) ?? keyType;
			if (underlyingType.IsEnum)
			{
				return Enum.Parse(underlyingType, key, true);
			}

			return Convert.ChangeType(key, underlyingType, CultureInfo.InvariantCulture);
		}

		private static bool IsSimpleType(Type type)
		{
			if (type.IsEnum)
			{
				return true;
			}

			switch (Type.GetTypeCode(type))
			{
				case TypeCode.Boolean:
				case TypeCode.Byte:
				case TypeCode.Char:
				case TypeCode.Decimal:
				case TypeCode.Double:
				case TypeCode.Int16:
				case TypeCode.Int32:
				case TypeCode.Int64:
				case TypeCode.SByte:
				case TypeCode.Single:
				case TypeCode.String:
				case TypeCode.UInt16:
				case TypeCode.UInt32:
				case TypeCode.UInt64:
					return true;
				default:
					return false;
			}
		}

		private static bool IsEnumerableType(Type type)
		{
			return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
		}

		private static bool TryGetDictionaryTypes(Type type, out Type keyType, out Type valueType)
		{
			Type dictionaryInterface = GetGenericInterface(type, typeof(IDictionary<,>));
			if (dictionaryInterface != null)
			{
				Type[] arguments = dictionaryInterface.GetGenericArguments();
				keyType = arguments[0];
				valueType = arguments[1];
				return true;
			}

			keyType = null;
			valueType = null;
			return false;
		}

		private static bool TryGetListElementType(Type type, out Type elementType)
		{
			Type listInterface = GetGenericInterface(type, typeof(IList<>)) ??
				GetGenericInterface(type, typeof(IEnumerable<>));
			if (listInterface != null)
			{
				elementType = listInterface.GetGenericArguments()[0];
				return true;
			}

			elementType = null;
			return false;
		}

		private static Type GetGenericInterface(Type type, Type genericTypeDefinition)
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition)
			{
				return type;
			}

			return type.GetInterfaces()
				.FirstOrDefault(interfaceType => interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == genericTypeDefinition);
		}

		private static IEnumerable<SerializableMember> GetSerializableMembers(Type type)
		{
			List<SerializableMember> members = new List<SerializableMember>();
			foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
				.Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
				.OrderBy(property => property.Name, StringComparer.Ordinal))
			{
				members.Add(new SerializableMember(property));
			}

			foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public)
				.OrderBy(field => field.Name, StringComparer.Ordinal))
			{
				members.Add(new SerializableMember(field));
			}

			return members;
		}

		private static IEnumerable<SerializedDictionaryEntry> EnumerateDictionaryEntries(IEnumerable dictionary)
		{
			foreach (object item in dictionary)
			{
				object key;
				object value;
				if (item is System.Collections.DictionaryEntry entry)
				{
					key = entry.Key;
					value = entry.Value;
				}
				else
				{
					Type itemType = item.GetType();
					key = itemType.GetProperty("Key").GetValue(item);
					value = itemType.GetProperty("Value").GetValue(item);
				}

				yield return new SerializedDictionaryEntry
				{
					KeyString = Convert.ToString(key, CultureInfo.InvariantCulture),
					Value = value
				};
			}
		}

		private sealed class FormatAdapter
		{
			public FormatAdapter(string format, Type modelType)
			{
				Format = format;
				ModelType = modelType;
			}

			public string Format { get; }
			public Type ModelType { get; }
		}

		private sealed class SerializableMember
		{
			private readonly PropertyInfo m_property;
			private readonly FieldInfo m_field;

			public SerializableMember(PropertyInfo property)
			{
				m_property = property;
				Name = property.Name;
				MemberType = property.PropertyType;
			}

			public SerializableMember(FieldInfo field)
			{
				m_field = field;
				Name = field.Name;
				MemberType = field.FieldType;
			}

			public string Name { get; }
			public Type MemberType { get; }

			public object GetValue(object instance)
			{
				return m_property != null ? m_property.GetValue(instance) : m_field.GetValue(instance);
			}

			public void SetValue(object instance, object value)
			{
				if (m_property != null)
				{
					m_property.SetValue(instance, value);
					return;
				}

				m_field.SetValue(instance, value);
			}
		}

		private sealed class SerializedDictionaryEntry
		{
			public string KeyString { get; set; }
			public object Value { get; set; }
		}
	}

	internal sealed class ImportedJsonDocument
	{
		public string Format { get; set; }
		public string FileName { get; set; }
		public object Model { get; set; }
	}
}
