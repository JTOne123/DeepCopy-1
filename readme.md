[![Build status](https://ci.appveyor.com/api/projects/status/17401ybvptlsvfy1?svg=true)](https://ci.appveyor.com/project/greuelpirat/deepcopy) [![nuget](https://img.shields.io/nuget/v/DeepCopy.Fody.svg)](https://www.nuget.org/packages/DeepCopy.Fody/)


## This is an add-in for [Fody](https://github.com/Fody/Home/)

![Icon](https://github.com/greuelpirat/DeepCopy/blob/master/package_icon.png)

Generate copy constructors and extension methods to create a new instance with deep copy of properties.

## Usage

See [Wiki](https://github.com/greuelpirat/DeepCopy/wiki)

See also [Fody usage](https://github.com/Fody/Home/blob/master/pages/usage.md).

### NuGet installation

Install the [DeepCopy.Fody NuGet package](https://nuget.org/packages/DeepCopy.Fody/) and update the [Fody NuGet package](https://nuget.org/packages/Fody/):

```powershell
PM> Install-Package Fody
PM> Install-Package DeepCopy.Fody
```

The `Install-Package Fody` is required since NuGet always defaults to the oldest, and most buggy, version of any dependency.

### Add to FodyWeavers.xml

Add `<DeepCopy/>` to [FodyWeavers.xml](https://github.com/Fody/Home/blob/master/pages/usage.md#add-fodyweaversxml)

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Weavers>
  <DeepCopy/>
</Weavers>
```

## Sample

#### Your Code
```csharp
public static class MyStaticClass
{
    [DeepCopyExtension]
    public static SomeObject DeepCopy(SomeObject source) => source;
}

public class SomeObject
{
    public int Integer { get; set; }
    public SomeEnum Enum { get; set; }
    public DateTime DateTime { get; set; }
    public string String { get; set; }
    public IList<SomeObject> List { get; set; }
    public IDictionary<SomeKey, SomeObject> Dictionary { get; set; }
}
```

#### What gets compiled
```csharp
public static class MyStaticClass
{
    [DeepCopyExtension]
    public static SomeObject DeepCopy(SomeObject source)
    {
        return source != null ? new SomeObject(source) : (SomeObject) null;
    }
}

public class SomeObject
{
    public SomeObject() { }
  
    public SomeObject(SomeObject obj)
    {
        this.Integer = obj.Integer;
        this.Enum = obj.Enum;
        this.DateTime = obj.DateTime;
        this.String = obj.String != null ? string.Copy(obj.String) : (string) null;
        if (obj.List != null) {
            this.List = (IList<SomeObject>) new System.Collections.Generic.List<SomeObject>();
            for (int index = 0; index < obj.List.Count; ++index)
                this.List.Add(obj.List[index] != null ? new SomeObject(obj0.List[index]) : (SomeObject) null);
        }
        if (obj.Dictionary != null) {
            this.Dictionary = (IDictionary<SomeKey, SomeObject>) new System.Collections.Generic.Dictionary<SomeKey, SomeObject>();
            foreach (KeyValuePair<SomeKey, SomeObject> keyValuePair in (IEnumerable<KeyValuePair<SomeKey, SomeObject>>) obj.Dictionary)
                this.Dictionary[new SomeKey(keyValuePair.Key)] = keyValuePair.Value != null ? new SomeObject(keyValuePair.Value) : (SomeObject) null;
        }
    }
  
    public int Integer { get; set; }
    public SomeEnum Enum { get; set; }
    public DateTime DateTime { get; set; }
    public string String { get; set; }
    public IList<SomeObject> List { get; set; }
}
```

## Icon

Icon copy by projecthayat  of [The Noun Project](http://thenounproject.com)