using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
using Newtonsoft.Json.Linq;

namespace KDWebServer
{
  public class QueryStringValuesCollection
  {
    public class Value
    {
      private readonly string _value;

      public Value(string value)
      {
        _value = value;
      }

      public bool IsNull => _value == null;
      public bool IsEmpty => AsString == "";
      public string AsString => _value;
      public int AsInt => int.Parse(_value);
      public JToken AsJson => JToken.Parse(_value);
    }

    public class ValuesCollection
    {
      private readonly List<Value> _values = new List<Value>();

      public void AddValue(Value v)
      {
        _values.Add(v);
      }

      public string GetString(int index = 0)
      {
        return _values[index].AsString;
      }

      public int GetInt(int index = 0)
      {
        string val = GetString(index);
        return int.Parse(val);
      }

      public Value GetSingleValue()
      {
        if (_values.Count > 1)
          throw new IndexOutOfRangeException("multiple elements");

        return _values[0];
      }

      public IEnumerable<Value> GetValues()
      {
        return _values;
      }
    }

    private readonly Dictionary<string, ValuesCollection> _valuesCollections = new Dictionary<string, ValuesCollection>();

    public static QueryStringValuesCollection FromNameValueCollection(NameValueCollection d)
    {
      var col = new QueryStringValuesCollection();
      foreach (string key in d.Keys) {
        var values = d.GetValues(key);
        if (values == null)
          continue;

        var valuesCollection = new ValuesCollection();

        if (key == null) {
          var value = values[0];
          string _key = value;
          valuesCollection.AddValue(new Value(null));
          col._valuesCollections[_key] = valuesCollection;
        }
        else {
          foreach (var value in values)
            valuesCollection.AddValue(new Value(value));
          col._valuesCollections[key] = valuesCollection;
        }
      }

      return col;
    }

    public static QueryStringValuesCollection Parse(string qs) => FromNameValueCollection(HttpUtility.ParseQueryString(qs));

    public IDictionary<string, string> GetAsDictionarySingleValues()
    {
      var dict = new Dictionary<string, string>();
      foreach (var valuesCollection in _valuesCollections)
        dict.Add(valuesCollection.Key, valuesCollection.Value.GetString());
      return dict;
    }

    public IDictionary<string, List<string>> GetAsDictionary()
    {
      var dict = new Dictionary<string, List<string>>();
      foreach (var valuesCollection in _valuesCollections)
        dict.Add(valuesCollection.Key, valuesCollection.Value.GetValues().Select(x => x.AsString).ToList());
      return dict;
    }

    public JObject ToJson()
    {
      var data = new JObject();

      foreach (var valuesCollection in _valuesCollections) {
        var valuesArray = new JArray(valuesCollection.Value.GetValues().Select(x => x.AsString));
        data.Add(new JProperty(valuesCollection.Key, valuesArray));
      }

      return data;
    }

    public Value GetValue(string name)
    {
      if (!_valuesCollections.ContainsKey(name))
        throw new KeyNotFoundException($"key /{name}/ is not present");

      return _valuesCollections[name].GetSingleValue();
    }

    public ValuesCollection GetValues(string name)
    {
      if (!_valuesCollections.ContainsKey(name))
        throw new IndexOutOfRangeException();

      return _valuesCollections[name];
    }

    public string GetString(string name)
    {
      if (!_valuesCollections.ContainsKey(name))
        throw new IndexOutOfRangeException();

      return _valuesCollections[name].GetString();
    }

    public int GetInt(string name)
    {
      string val = GetString(name);
      return int.Parse(val);
    }

    public bool Contains(string name)
    {
      return _valuesCollections.ContainsKey(name);
    }

    public Value this[string name] => GetValue(name);

    public string GetStringOrDefault(string name, string @default = null)
    {
      return _valuesCollections.ContainsKey(name) ? GetString(name) : @default;
    }
  }
}