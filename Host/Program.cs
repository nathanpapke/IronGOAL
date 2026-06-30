using IronScheme;
using IronGOAL;

var config = new GoalRuntimeConfig
{
    GlobalHeapSize        = 16 * 1024 * 1024,
    StackHeapSize         =  2 * 1024 * 1024,
    TransformChannelCapacity = 64,
    EnableMemoryTracking  = false,
    EnableDebugChannel    = false,
    LogHandler            = (sev, code, msg) => Console.WriteLine($"[{sev}] {code}: {msg}")
};

var result = Host.Create(config);
if (result.IsFailure)
{
    Console.WriteLine($"Host.Create failed: {result.ErrorMessage}");
    return;
}

void Probe(string label, Func<object?> action)
{
    try
    {
        object? value = action();
        Console.WriteLine($"{label}: OK -> {value} ({value?.GetType().FullName ?? "null"})");
    }
    catch (Exception ex)
    {
        string message = ex.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        Console.WriteLine($"{label}: THREW {ex.GetType().Name}\r\n{message}");
    }
}

object env = "(interaction-environment)".Eval();

// ---- 1. read loop + eval landing in the shared environment ----
object? d1 = null, d2 = null, d3 = null;
Probe("read+eval round trip", () =>
{
    object port = "(open-string-input-port {0})".Eval("(define probe-a 1) (+ probe-a 41)");
    d1 = "(read {0})".Eval(port);
    d2 = "(read {0})".Eval(port);
    d3 = "(read {0})".Eval(port);

    bool eof3 = (bool)"(eof-object? {0})".Eval(d3);
    object v1 = "(eval {0} {1})".Eval(d1, env);
    object v2 = "(eval {0} {1})".Eval(d2, env);

    return $"d1={d1} ({d1!.GetType().Name}), d2={d2} ({d2!.GetType().Name}), eof3={eof3}, v1={v1}, v2={v2}";
});

Probe("probe-a visible via plain Eval after eval-with-env", () => "probe-a".Eval());

// ---- 2. write / open-output-string / get-output-string for previews ----
Probe("write datum to string", () =>
{
    object sp = "(open-output-string)".Eval();
    "(write {0} {1})".Eval(d1, sp);
    return "(get-output-string {0})".Eval(sp);
});

// ---- 3. guard + quasiquote-with-substitution ----
Probe("guard available, quasiquote splices datum", () =>
{
    // Build (guard (e (#t 'caught)) ,d2)  -- d2 is (+ probe-a 41), should NOT trigger guard
    object form = "`(guard (e (#t 'caught)) ,{0})".Eval(d2);
    return "(eval {0} {1})".Eval(form, env);  // expect 42
});

Probe("guard catches an error", () =>
{
    object badDatum = "(car '())".Eval<object>(); // wait, need the datum not the result
    return "(eval `(guard (e (#t 'caught)) (car '())) {0})".Eval(env);
});

// ---- 4. dispatch: (pair? d) / (car d) / memq ----
Probe("car of define-form, memq dispatch", () =>
{
    object isDefine = "(and (pair? {0}) (memq (car {0}) '(define begin deftype)) #t)".Eval(d1);
    object isNotDefine = "(and (pair? {0}) (memq (car {0}) '(define begin deftype)) #t)".Eval(d2);
    return $"d1 is binding-form: {isDefine}, d2 is binding-form: {isNotDefine}";
});

// ---- 5. read on malformed input ----
Probe("read on stray close paren", () =>
{
    object port = "(open-string-input-port {0})".Eval("(foo))");
    object first = "(read {0})".Eval(port);   // (foo) - fine
    object second = "(read {0})".Eval(port);  // should throw on the stray )
    return $"first={first}, second={second}";
});

Probe("read on unterminated list", () =>
{
    object port = "(open-string-input-port {0})".Eval("(foo (bar)");
    return "(read {0})".Eval(port);
});

Console.WriteLine("Done.");
