A .NET wrapper that loads `frida-gadget.dll` into a target executable by launching it in suspended mode, injecting the DLL, and resuming the process—ensuring the gadget runs before the application logic starts.

---

## 🔧 Setup

1. Open the file `DotNet.DllInjector/Program.cs`.
2. Edit the `OriginalExeFileName` constant so it matches your original executable:

```csharp
// CHANGE THIS! It must match your original executable name.
private const string OriginalExeFileName = "app_original.exe";
```

## 🚀 Build

Use `dotnet publish` to generate the final executable. `Debug` includes a console window; `Release` does not:

```bash
dotnet publish -c Release -r win-x64 --self-contained true
# or
dotnet publish -c Debug -r win-x64 --self-contained true
```

* `-c`: `Release`, `Debug`
* `-r`: Target architecture (`win-x64` or `win-x86`)
* `--self-contained true`: Bundles the .NET runtime into the executable

> The resulting files will be located in:
>
> * `bin/Release/net9.0/win-x64/publish/`
> * `bin/Debug/net9.0/win-x64/publish/`

## 📖 Usage

In your application's folder:

1. **Rename the original executable**: e.g., from `app.exe` to `app_original.exe`
2. **Copy the wrapper**: Paste the compiled injector executable (e.g., `DotNet.DllInjector.exe`) into this folder and rename it to match the original executable name (e.g., `app.exe`)
3. **Add the gadget**: Copy `frida-gadget.dll` into the same folder

Done! Launching `app.exe` runs the wrapper, injects the gadget early, then resumes the app. More info: [https://frida.re/docs/gadget/](https://frida.re/docs/gadget/)

---

## 📄 License

This project is licensed under the MIT License. See the `LICENSE` file for details.
