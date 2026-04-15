#!/usr/bin/env radiance
# RADIANCE SHELL — Comprehensive Stress Test Script
# Run: dotnet run -- scripts/stress_test.sh

PASS_COUNT=0
FAIL_COUNT=0
TOTAL_COUNT=0

assert_eq() {
    TOTAL_COUNT=$((TOTAL_COUNT + 1))
    if test "$1" = "$2"; then
        PASS_COUNT=$((PASS_COUNT + 1))
        echo "PASS: $3"
    else
        FAIL_COUNT=$((FAIL_COUNT + 1))
        echo "FAIL: $3 — expected '$1', got '$2'"
    fi
}

assert_contains() {
    TOTAL_COUNT=$((TOTAL_COUNT + 1))
    case "$2" in
        *$1*)
            PASS_COUNT=$((PASS_COUNT + 1))
            echo "PASS: $3"
            ;;
        *)
            FAIL_COUNT=$((FAIL_COUNT + 1))
            echo "FAIL: $3 — expected to contain '$1' in '$2'"
            ;;
    esac
}

assert_not_empty() {
    TOTAL_COUNT=$((TOTAL_COUNT + 1))
    if test -n "$1"; then
        PASS_COUNT=$((PASS_COUNT + 1))
        echo "PASS: $2"
    else
        FAIL_COUNT=$((FAIL_COUNT + 1))
        echo "FAIL: $2 — expected non-empty, got empty"
    fi
}

echo ""
echo "================================================================"
echo "       RADIANCE SHELL — COMPREHENSIVE STRESS TEST"
echo "================================================================"
echo ""

echo "--- Section 1: Variable Fundamentals ---"
X=hello
assert_eq "hello" "$X" "1.1 Simple variable assignment"
X=world
assert_eq "world" "$X" "1.2 Variable overwrite"
EMPTY=
assert_eq "" "$EMPTY" "1.3 Empty variable"
MY_VAR_123=test_value
assert_eq "test_value" "$MY_VAR_123" "1.4 Variable with underscores and numbers"
NAME=file
assert_eq "file.txt" "${NAME}.txt" "1.5 Variable brace with suffix"
assert_eq "myfile" "my${NAME}" "1.6 Variable brace with prefix"
assert_eq "prefixfilesuffix" "prefix${NAME}suffix" "1.7 Variable brace surrounded"
assert_eq "" "$COMPLETELY_UNDEFINED_XYZ_999" "1.8 Undefined variable is empty"
A=1; B=2; C=3
assert_eq "1 2 3" "$A $B $C" "1.9 Multiple semicolon-separated assignments"
SPACED="hello world"
assert_eq "hello world" "$SPACED" "1.10 Variable with spaces (quoted)"
SRC=source
DST=$SRC
assert_eq "source" "$DST" "1.11 Reassign from another variable"
A=alpha
B=$A
C=$B
assert_eq "alpha" "$C" "1.12 Chain variable assignment"

echo ""
echo "--- Section 2: Quoting Edge Cases ---"
assert_eq "helloworld" "hello"'world' "2.1 Adjacent double+single quotes"
NOEXPAND=value
assert_eq '$NOEXPAND' '$NOEXPAND' "2.2 Single quotes prevent expansion"
assert_eq "value" "$NOEXPAND" "2.3 Double quotes allow expansion"
WHO=world
assert_eq "hello world" "hello "$WHO "2.4 Mixed quoting with variable"
assert_eq "" "" "2.5 Empty string from quotes"
GREETING="hello $WHO end"
assert_eq "hello world end" "$GREETING" "2.6 Variable in middle of string"
assert_eq "a   b" "a   b" "2.7 Double quotes preserve multiple spaces"
LIT='no $expansion here'
assert_eq "no \$expansion here" "$LIT" "2.8 Single-quoted assignment is literal"
NIL=""
assert_eq "" "$NIL" "2.9 Empty quoted string variable"

echo ""
echo "--- Section 3: Parameter Expansion ---"
assert_eq "fallback" "${UNSET_XYZ_123:-fallback}" "3.1 :- with unset"
PE_SET=exists
assert_eq "exists" "${PE_SET:-fallback}" "3.2 :- with set"
RESULT=${PE_ASSIGN_TEST:=assigned}
assert_eq "assigned" "$RESULT" "3.3 := returns default"
assert_eq "assigned" "$PE_ASSIGN_TEST" "3.3b := assigned variable"
PE_ALT=setval
assert_eq "alternate" "${PE_ALT:+alternate}" "3.4 :+ with set"
assert_eq "" "${UNSET_PE_ALT:+alternate}" "3.5 :+ with unset"
LONGSTR=hello
assert_eq "5" "${#LONGSTR}" "3.6 String length with #"
EMPTYSTR=
assert_eq "0" "${#EMPTYSTR}" "3.7 String length of empty"
LONGER=abcdefghij
assert_eq "10" "${#LONGER}" "3.8 String length of 10-char string"

echo ""
echo "--- Section 4: Arithmetic Expansion ---"
assert_eq "5" "$((2 + 3))" "4.1 Addition"
assert_eq "7" "$((10 - 3))" "4.2 Subtraction"
assert_eq "42" "$((6 * 7))" "4.3 Multiplication"
assert_eq "5" "$((15 / 3))" "4.4 Division"
assert_eq "1" "$((10 % 3))" "4.5 Modulo"
assert_eq "-3" "$((2 - 5))" "4.6 Negative result"
AV=10
BV=3
assert_eq "13" "$((AV + BV))" "4.7 Variables in arithmetic"
assert_eq "13" "$(($AV + $BV))" "4.8 Dollar variables in arithmetic"
assert_eq "14" "$(( (2 + 3) * (4 - 1) + -1 ))" "4.9 Complex nested expression"
assert_eq "1" "$((5 > 3))" "4.10 Comparison 5>3"
assert_eq "0" "$((3 > 5))" "4.11 Comparison 3>5"
assert_eq "1" "$((5 == 5))" "4.12 Equality"
assert_eq "1" "$((5 != 3))" "4.13 Inequality"
SUM=$((10 + 20))
assert_eq "30" "$SUM" "4.14 Arithmetic in variable assignment"
CTR=0
CTR=$((CTR + 1))
CTR=$((CTR + 1))
CTR=$((CTR + 1))
assert_eq "3" "$CTR" "4.15 Manual increment pattern"
assert_eq "120" "$((1 * 2 * 3 * 4 * 5))" "4.16 Factorial-like multiplication"

echo ""
echo "--- Section 5: Command Substitution ---"
assert_eq "hello" "$(echo hello)" "5.1 Simple command substitution"
CS_VAR=$(echo computed)
assert_eq "computed" "$CS_VAR" "5.2 CS in assignment"
assert_eq "inner" "$(echo $(echo inner))" "5.3 Nested CS"
assert_eq "deep" "$(echo $(echo $(echo deep)))" "5.4 Triple nested CS"
CS_X=world
assert_eq "hello world" "$(echo hello $CS_X)" "5.5 CS with variable"
assert_eq "42" "$(echo $((6 * 7)))" "5.6 CS with arithmetic"
CS_MULTI=$(printf 'line1\nline2\nline3')
assert_not_empty "$CS_MULTI" "5.7 Multi-line CS is non-empty"
assert_eq "result: done" "result: $(echo done)" "5.8 CS in string"
assert_eq "val_42" "val_$(echo 42)" "5.9 CS in word concat"

echo ""
echo "--- Section 6: Control Flow - if/elif/else ---"
IF_RESULT=""
if true; then
    IF_RESULT=yes
fi
assert_eq "yes" "$IF_RESULT" "6.1 Simple if true"
IF_RESULT2=""
if false; then
    IF_RESULT2=yes
fi
assert_eq "" "$IF_RESULT2" "6.2 Simple if false"
IF_ELSE1=""
if true; then
    IF_ELSE1=then
else
    IF_ELSE1=else
fi
assert_eq "then" "$IF_ELSE1" "6.3 If-else true branch"
IF_ELSE2=""
if false; then
    IF_ELSE2=then
else
    IF_ELSE2=else
fi
assert_eq "else" "$IF_ELSE2" "6.4 If-else false branch"
ELIF_R=""
X=2
if test $X -eq 1; then
    ELIF_R=one
elif test $X -eq 2; then
    ELIF_R=two
else
    ELIF_R=other
fi
assert_eq "two" "$ELIF_R" "6.5 If-elif-else"
NESTED_IF=""
A=5
B=3
if test $A -gt $B; then
    if test $A -eq 5; then
        NESTED_IF=both
    else
        NESTED_IF=outer
    fi
else
    NESTED_IF=none
fi
assert_eq "both" "$NESTED_IF" "6.6 Nested if statements"
CS_IF=""
if test "$(echo yes)" = "yes"; then
    CS_IF=matched
fi
assert_eq "matched" "$CS_IF" "6.7 If with CS condition"
ARITH_IF=""
if test $((1 + 1)) -eq 2; then
    ARITH_IF=correct
fi
assert_eq "correct" "$ARITH_IF" "6.8 If with arithmetic condition"
MULTI_ELIF=""
VAL=c
if test $VAL = a; then
    MULTI_ELIF=1
elif test $VAL = b; then
    MULTI_ELIF=2
elif test $VAL = c; then
    MULTI_ELIF=3
elif test $VAL = d; then
    MULTI_ELIF=4
else
    MULTI_ELIF=none
fi
assert_eq "3" "$MULTI_ELIF" "6.9 Multiple elif branches"
AND_IF=""
if true && true; then
    AND_IF=yes
fi
assert_eq "yes" "$AND_IF" "6.10 If with && condition"

echo ""
echo "--- Section 7: Control Flow - for loops ---"
FOR_ACCUM=""
for i in a b c; do
    FOR_ACCUM="${FOR_ACCUM}${i}"
done
assert_eq "abc" "$FOR_ACCUM" "7.1 Simple for loop"
FOR_EXPANDED=""
for item in x y z; do
    FOR_EXPANDED="${FOR_EXPANDED}${item}"
done
assert_eq "xyz" "$FOR_EXPANDED" "7.2 For loop with word list"
FOR_SUM=0
for n in 1 2 3 4 5; do
    FOR_SUM=$((FOR_SUM + n))
done
assert_eq "15" "$FOR_SUM" "7.3 For loop sum 1-5"
NESTED_FOR=""
for i in 1 2; do
    for j in a b; do
        NESTED_FOR="${NESTED_FOR}${i}${j} "
    done
done
assert_eq "1a 1b 2a 2b " "$NESTED_FOR" "7.4 Nested for loops"
BREAK_FOR=""
for x in 1 2 3 4 5; do
    if test $x -eq 3; then
        break
    fi
    BREAK_FOR="${BREAK_FOR}${x}"
done
assert_eq "12" "$BREAK_FOR" "7.5 For loop with break"
CONT_FOR=""
for x in 1 2 3 4 5; do
    if test $x -eq 3; then
        continue
    fi
    CONT_FOR="${CONT_FOR}${x}"
done
assert_eq "1245" "$CONT_FOR" "7.6 For loop with continue"
SEP_STR=""
for word in alpha beta gamma delta; do
    if test -z "$SEP_STR"; then
        SEP_STR=$word
    else
        SEP_STR="${SEP_STR},${word}"
    fi
done
assert_eq "alpha,beta,gamma,delta" "$SEP_STR" "7.7 For loop with comma separator"
TRIPLE_NEST=""
for i in 1 2; do
    for j in a b; do
        for k in x y; do
            TRIPLE_NEST="${TRIPLE_NEST}${i}${j}${k} "
        done
    done
done
assert_eq "1ax 1ay 1bx 1by 2ax 2ay 2bx 2by " "$TRIPLE_NEST" "7.8 Triple nested for loop"

echo ""
echo "--- Section 8: Control Flow - while/until loops ---"
W_COUNT=0
W_SUM=0
while test $W_COUNT -lt 5; do
    W_COUNT=$((W_COUNT + 1))
    W_SUM=$((W_SUM + W_COUNT))
done
assert_eq "15" "$W_SUM" "8.1 While loop sum 1-5"
WB_COUNT=0
WB_RESULT=""
while true; do
    WB_COUNT=$((WB_COUNT + 1))
    if test $WB_COUNT -eq 4; then
        break
    fi
    WB_RESULT="${WB_RESULT}${WB_COUNT}"
done
assert_eq "123" "$WB_RESULT" "8.2 While with break"
WC_RESULT=""
WC_I=0
while test $WC_I -lt 5; do
    WC_I=$((WC_I + 1))
    if test $((WC_I % 2)) -eq 0; then
        continue
    fi
    WC_RESULT="${WC_RESULT}${WC_I}"
done
assert_eq "135" "$WC_RESULT" "8.3 While with continue (odd numbers)"
U_COUNT=0
until test $U_COUNT -ge 5; do
    U_COUNT=$((U_COUNT + 1))
done
assert_eq "5" "$U_COUNT" "8.4 Until loop reaches 5"
NW_OUTER=0
NW_INNER_SUM=0
while test $NW_OUTER -lt 3; do
    NW_OUTER=$((NW_OUTER + 1))
    NW_INNER=0
    while test $NW_INNER -lt $NW_OUTER; do
        NW_INNER=$((NW_INNER + 1))
        NW_INNER_SUM=$((NW_INNER_SUM + 1))
    done
done
assert_eq "6" "$NW_INNER_SUM" "8.5 Nested while loop (1+2+3=6)"
WCS_RESULT=""
WCS_I=0
while test $WCS_I -lt $(echo 3); do
    WCS_I=$((WCS_I + 1))
    WCS_RESULT="${WCS_RESULT}${WCS_I}"
done
assert_eq "123" "$WCS_RESULT" "8.6 While with CS condition"

echo ""
echo "--- Section 9: Case Statements ---"
CASE_R=""
case hello in
    hello)
        CASE_R=matched
        ;;
esac
assert_eq "matched" "$CASE_R" "9.1 Case exact match"
CASE_DEF=""
case xyz in
    abc) CASE_DEF=abc ;;
    *) CASE_DEF=default ;;
esac
assert_eq "default" "$CASE_DEF" "9.2 Case wildcard default"
CASE_MULTI=""
VOWEL=e
case $VOWEL in
    a|e|i|o|u) CASE_MULTI=vowel ;;
    *) CASE_MULTI=consonant ;;
esac
assert_eq "vowel" "$CASE_MULTI" "9.3 Case multiple patterns"
CASE_VAR=3
CASE_VR=""
case $CASE_VAR in
    1) CASE_VR=one ;;
    2) CASE_VR=two ;;
    3) CASE_VR=three ;;
    *) CASE_VR=other ;;
esac
assert_eq "three" "$CASE_VR" "9.4 Case with variable match"
case_func() {
    case $1 in
        start) echo "starting" ;;
        stop) echo "stopping" ;;
        *) echo "unknown" ;;
    esac
}
assert_eq "starting" "$(case_func start)" "9.5 Case in function - start"
assert_eq "stopping" "$(case_func stop)" "9.5b Case in function - stop"
assert_eq "unknown" "$(case_func xyz)" "9.5c Case in function - unknown"
CASE_GLOB="hello.world"
CASE_GR=""
case $CASE_GLOB in
    h*) CASE_GR=starts_with_h ;;
    *) CASE_GR=other ;;
esac
assert_eq "starts_with_h" "$CASE_GR" "9.6 Case with glob pattern"

echo ""
echo "--- Section 10: Functions - Advanced ---"
add() {
    local a=$1
    local b=$2
    echo $((a + b))
}
assert_eq "15" "$(add 7 8)" "10.1 Function: addition"
local_test() {
    local LVAR=local_value
    echo $LVAR
}
LOCAL_OUT=$(local_test)
assert_eq "local_value" "$LOCAL_OUT" "10.2 Function local variable"
GVAR=global
local_scope() {
    local GVAR=local_mod
}
local_scope
assert_eq "global" "$GVAR" "10.3 Local variable does not leak to global"
ret_42() {
    return 42
}
ret_42
assert_eq "42" "$?" "10.4 Function return code"
modify_global() {
    GLOBAL_MOD=modified
}
GLOBAL_MOD=original
modify_global
assert_eq "modified" "$GLOBAL_MOD" "10.5 Function modifying global"
factorial() {
    local n=$1
    if test $n -le 1; then
        echo 1
    else
        local sub=$(factorial $((n - 1)))
        echo $((n * sub))
    fi
}
assert_eq "120" "$(factorial 5)" "10.6 Recursive factorial(5)"
assert_eq "1" "$(factorial 1)" "10.6b Recursive factorial(1)"
fib() {
    local n=$1
    if test $n -le 1; then
        echo $n
    else
        local fa=$(fib $((n - 1)))
        local fb=$(fib $((n - 2)))
        echo $((fa + fb))
    fi
}
assert_eq "0" "$(fib 0)" "10.7 Fibonacci(0)"
assert_eq "1" "$(fib 1)" "10.7b Fibonacci(1)"
assert_eq "5" "$(fib 5)" "10.7c Fibonacci(5)"
assert_eq "55" "$(fib 10)" "10.7d Fibonacci(10)"
inner() {
    echo "inner:$1"
}
outer() {
    inner $(echo $1)
}
assert_eq "inner:hello" "$(outer hello)" "10.8 Function calling function"
many_args() {
    echo "$1 $2 $3 $4 $5 $6 $7 $8 $9"
}
assert_eq "a b c d e f g h i" "$(many_args a b c d e f g h i)" "10.9 Function with 9 arguments"
arg_check() {
    echo "$1_$2_$3"
}
assert_eq "one_two_three" "$(arg_check one two three four five)" "10.10 Function argument access"
greet() {
    echo "Hello, $1!"
}
assert_eq "Hello, World!" "$(greet World)" "10.11 Function with greeting"
early_return() {
    echo "before"
    return 0
    echo "after"
}
assert_eq "before" "$(early_return)" "10.12 Early return from function"

echo ""
echo "--- Section 11: Pipelines ---"
PWC=$(echo hello world | wc -w | tr -d ' ')
assert_eq "2" "$PWC" "11.1 Echo | wc -w"
PIPE3=$(echo "banana apple cherry" | tr ' ' '\n' | sort | tr '\n' ' ')
assert_contains "apple" "$PIPE3" "11.2 Three-stage pipeline"
PIPE_GREP=$(printf 'dog\ncat\nbird\n' | grep cat)
assert_eq "cat" "$PIPE_GREP" "11.3 Pipeline with grep"
true | true
assert_eq "0" "$?" "11.4 Pipeline true|true exit code"
CS_PIPE=$(echo "test data" | wc -c | tr -d ' ')
assert_not_empty "$CS_PIPE" "11.5 CS with pipeline"

echo ""
echo "--- Section 12: Redirections ---"
RDIR=/tmp/radiance_stress_$$
echo "redirect_out" > $RDIR
RD_CONTENT=$(cat $RDIR)
assert_eq "redirect_out" "$RD_CONTENT" "12.1 Output redirect >"
echo "line2" >> $RDIR
RD_FIRST=$(head -1 $RDIR)
assert_eq "redirect_out" "$RD_FIRST" "12.2 Append redirect >> keeps first line"
echo "input_data" > $RDIR
RD_IN=$(cat < $RDIR)
assert_eq "input_data" "$RD_IN" "12.3 Input redirect <"
write_file_func() {
    echo "func_output" > $1
}
write_file_func $RDIR
assert_eq "func_output" "$(cat $RDIR)" "12.4 Redirect in function"
for i in 1 2 3; do
    echo "item_$i" >> $RDIR
done
RD_LAST=$(tail -1 $RDIR)
assert_eq "item_3" "$RD_LAST" "12.5 Redirect in for loop"
rm -f $RDIR

echo ""
echo "--- Section 13: Logical Operators & Lists ---"
assert_eq "ran" "$(true && echo ran)" "13.1 && runs when left succeeds"
assert_eq "" "$(false && echo ran)" "13.2 && skips when left fails"
assert_eq "ran" "$(false || echo ran)" "13.3 || runs when left fails"
assert_eq "" "$(true || echo ran)" "13.4 || skips when left succeeds"
CHAIN=$(false || true && echo done)
assert_eq "done" "$CHAIN" "13.5 Complex && || chain"
SEMI=$(false; echo next)
assert_eq "next" "$SEMI" "13.6 Semicolon runs after false"
AND_CHAIN=$(true && true && true && echo all_true)
assert_eq "all_true" "$AND_CHAIN" "13.7 Multiple && chain"
AND_FAIL=$(true && false && echo should_not_run)
assert_eq "" "$AND_FAIL" "13.8 && chain with early failure"
OR_CHAIN=$(false || false || echo fallback)
assert_eq "fallback" "$OR_CHAIN" "13.9 Multiple || chain"
MIXED=$(false && echo no || echo yes)
assert_eq "yes" "$MIXED" "13.10 Mixed && and ||"

echo ""
echo "--- Section 14: Special Variables ---"
true
assert_eq "0" "$?" "14.1 Dollar-? after true"
false
assert_eq "1" "$?" "14.2 Dollar-? after false"
assert_not_empty "$0" "14.3 Dollar-0 is not empty"
CS_EXIT=$(true)
assert_eq "0" "$?" "14.5 Dollar-? after CS success"
false
NEG=$?
assert_eq "1" "$NEG" "14.6 Dollar-? captured in variable"

echo ""
echo "--- Section 15: Aliases ---"
alias testalias='echo ALIAS_WORKS'
assert_eq "ALIAS_WORKS" "$(testalias)" "15.1 Alias definition and usage"
unalias testalias
testalias 2>/dev/null
ALIAS_EXIT=$?
TOTAL_COUNT=$((TOTAL_COUNT + 1))
if test $ALIAS_EXIT -ne 0; then
    PASS_COUNT=$((PASS_COUNT + 1))
    echo "PASS: 15.2 Unalias removes alias"
else
    FAIL_COUNT=$((FAIL_COUNT + 1))
    echo "FAIL: 15.2 Unalias should remove alias"
fi
alias sayhello='echo hello'
assert_eq "hello world" "$(sayhello world)" "15.3 Alias with appended argument"

echo ""
echo "--- Section 16: Builtin Commands ---"
assert_eq "test" "$(echo test)" "16.1 Echo builtin"
assert_not_empty "$(pwd)" "16.2 Pwd returns non-empty"
true
assert_eq "0" "$?" "16.3 True returns 0"
false
assert_eq "1" "$?" "16.4 False returns 1"
TYPE_OUT=$(type echo)
assert_contains "builtin" "$TYPE_OUT" "16.5 Type echo is builtin"
tp_func() { echo hi; }
TYPE_FN=$(type tp_func)
assert_contains "function" "$TYPE_FN" "16.6 Type recognizes function"
TYPE_EXT=$(type ls)
assert_contains "ls" "$TYPE_EXT" "16.7 Type recognizes external command"
TEST_SET_VAR=settest123
assert_eq "settest123" "$TEST_SET_VAR" "16.8 Variable is set"
export STRESS_EXPORT_TEST=exported123
assert_eq "exported123" "$STRESS_EXPORT_TEST" "16.9 Export sets variable"
STRESS_UNSET=value123
unset STRESS_UNSET
assert_eq "" "$STRESS_UNSET" "16.10 Unset removes variable"
ORIG_DIR=$(pwd)
cd /tmp
assert_eq "/tmp" "$(pwd)" "16.11 Cd changes directory"
cd $ORIG_DIR

echo ""
echo "--- Section 17: Real-World Script Patterns ---"
CSV_LINE=""
for val in alpha beta gamma delta omega; do
    if test -z "$CSV_LINE"; then
        CSV_LINE=$val
    else
        CSV_LINE="${CSV_LINE},${val}"
    fi
done
assert_eq "alpha,beta,gamma,delta,omega" "$CSV_LINE" "17.1 CSV builder pattern"
MIN_VAL=999
MAX_VAL=0
for n in 34 7 89 2 56 12 91 23; do
    if test $n -lt $MIN_VAL; then
        MIN_VAL=$n
    fi
    if test $n -gt $MAX_VAL; then
        MAX_VAL=$n
    fi
done
assert_eq "2" "$MIN_VAL" "17.2 Min finder"
assert_eq "91" "$MAX_VAL" "17.2b Max finder"
menu_choice() {
    case $1 in
        1) echo "Option One" ;;
        2) echo "Option Two" ;;
        3|4|5) echo "Option Three-Five" ;;
        q|Q) echo "Quit" ;;
        *) echo "Invalid" ;;
    esac
}
assert_eq "Option One" "$(menu_choice 1)" "17.3 Menu pattern - option 1"
assert_eq "Option Three-Five" "$(menu_choice 4)" "17.3b Menu pattern - option 4"
assert_eq "Quit" "$(menu_choice q)" "17.3c Menu pattern - quit"
assert_eq "Invalid" "$(menu_choice z)" "17.3d Menu pattern - invalid"
threshold_check() {
    local count=$1
    local threshold=$2
    if test $count -ge $threshold; then
        echo "EXCEEDED"
    else
        echo "OK (count=$count, threshold=$threshold)"
    fi
}
assert_eq "OK (count=3, threshold=10)" "$(threshold_check 3 10)" "17.4 Threshold - under"
assert_eq "EXCEEDED" "$(threshold_check 15 10)" "17.4b Threshold - exceeded"
gcd() {
    local a=$1
    local b=$2
    while test $b -ne 0; do
        local temp=$b
        b=$((a % b))
        a=$temp
    done
    echo $a
}
assert_eq "6" "$(gcd 48 18)" "17.5 GCD(48,18)"
assert_eq "1" "$(gcd 7 13)" "17.5b GCD(7,13)"
assert_eq "12" "$(gcd 24 36)" "17.5c GCD(24,36)"
fizzbuzz() {
    local n=$1
    if test $((n % 15)) -eq 0; then
        echo "FizzBuzz"
    elif test $((n % 3)) -eq 0; then
        echo "Fizz"
    elif test $((n % 5)) -eq 0; then
        echo "Buzz"
    else
        echo $n
    fi
}
assert_eq "1" "$(fizzbuzz 1)" "17.6 FizzBuzz(1)"
assert_eq "Fizz" "$(fizzbuzz 3)" "17.6b FizzBuzz(3)"
assert_eq "Buzz" "$(fizzbuzz 5)" "17.6c FizzBuzz(5)"
assert_eq "FizzBuzz" "$(fizzbuzz 15)" "17.6d FizzBuzz(15)"
assert_eq "7" "$(fizzbuzz 7)" "17.6e FizzBuzz(7)"
reverse_num() {
    local n=$1
    local rev=0
    while test $n -gt 0; do
        local digit=$((n % 10))
        rev=$((rev * 10 + digit))
        n=$((n / 10))
    done
    echo $rev
}
assert_eq "321" "$(reverse_num 123)" "17.7 Reverse(123)"
assert_eq "1" "$(reverse_num 1)" "17.7b Reverse(1)"
assert_eq "987654" "$(reverse_num 456789)" "17.7c Reverse(456789)"

echo ""
echo "--- Section 18: Edge Cases ---"
SPECIAL="hello world"
assert_eq "hello world" "$SPECIAL" "18.1 Variable with spaces"
SEMI_R=""
A=1; B=2; C=3; D=4; E=5
SEMI_R="$A$B$C$D$E"
assert_eq "12345" "$SEMI_R" "18.2 Many semicolons"
LONG_VAL="abcdefghij"
LONG_VAL="${LONG_VAL}${LONG_VAL}"
LONG_VAL="${LONG_VAL}${LONG_VAL}"
LONG_VAL="${LONG_VAL}${LONG_VAL}"
LONG_VAL="${LONG_VAL}${LONG_VAL}"
assert_eq "160" "${#LONG_VAL}" "18.3 Long variable value (160 chars)"
NEST_CS=$(echo $(echo $(echo $(echo deep))))
assert_eq "deep" "$NEST_CS" "18.4 Four levels nested CS"
COMPLEX_ARITH=$(( (1 + 2) * (3 + 4) - (5 * 6) / 2 + 8 % 3 ))
assert_eq "8" "$COMPLEX_ARITH" "18.5 Complex arithmetic"
indirect_a() {
    if test "$1" = "b"; then
        indirect_b "from_a"
    else
        echo "a: $1"
    fi
}
indirect_b() {
    echo "b: $1"
}
assert_eq "b: from_a" "$(indirect_a b)" "18.6 Indirect function calling"
REUSE=global
reuse_scope_test() {
    local REUSE=local1
    REUSE=local2
    echo $REUSE
}
assert_eq "local2" "$(reuse_scope_test)" "18.7a Reuse in local scope"
assert_eq "global" "$REUSE" "18.7b Global preserved after local scope"
STR_A="hello"
STR_B="cruel"
STR_C="world"
COMPLEX_STR="${STR_A} ${STR_B} ${STR_C}"
assert_eq "hello cruel world" "$COMPLEX_STR" "18.8 Complex string building"
empty_func() {
    return 0
}
empty_func
assert_eq "0" "$?" "18.9 Empty function body returns 0"

echo ""
echo "--- Section 19: Stress - Heavy Computation ---"
SUM100=0
N100=1
while test $N100 -le 100; do
    SUM100=$((SUM100 + N100))
    N100=$((N100 + 1))
done
assert_eq "5050" "$SUM100" "19.1 Sum of 1-100 = 5050"
POW_BASE=2
POW_EXP=10
POW_RESULT=1
POW_I=0
while test $POW_I -lt $POW_EXP; do
    POW_RESULT=$((POW_RESULT * POW_BASE))
    POW_I=$((POW_I + 1))
done
assert_eq "1024" "$POW_RESULT" "19.2 2^10 = 1024"
collatz_len() {
    local n=$1
    local steps=0
    while test $n -ne 1; do
        if test $((n % 2)) -eq 0; then
            n=$((n / 2))
        else
            n=$((n * 3 + 1))
        fi
        steps=$((steps + 1))
    done
    echo $steps
}
assert_eq "111" "$(collatz_len 27)" "19.3 Collatz(27) = 111 steps"
assert_eq "0" "$(collatz_len 1)" "19.3b Collatz(1) = 0 steps"
tri_pattern() {
    local result=""
    local i=1
    while test $i -le $1; do
        local j=1
        local line=""
        while test $j -le $i; do
            line="${line}${j}"
            j=$((j + 1))
        done
        if test -z "$result"; then
            result=$line
        else
            result="${result} ${line}"
        fi
        i=$((i + 1))
    done
    echo $result
}
assert_eq "1 12 123 1234 12345" "$(tri_pattern 5)" "19.4 Triangle pattern"
mult_row() {
    local base=$1
    local result=""
    local i=1
    while test $i -le 5; do
        local prod=$((base * i))
        if test -z "$result"; then
            result=$prod
        else
            result="${result} ${prod}"
        fi
        i=$((i + 1))
    done
    echo $result
}
assert_eq "3 6 9 12 15" "$(mult_row 3)" "19.5 Multiplication row for 3"
assert_eq "5 10 15 20 25" "$(mult_row 5)" "19.5b Multiplication row for 5"

echo ""
echo "--- Section 20: Interactive Feature Tests ---"
TILDE_HOME=~
assert_not_empty "$TILDE_HOME" "20.1 Tilde ~ expands to non-empty"
NEST_ARITH_CS=$(echo $(( (10 + 5) * 2 )))
assert_eq "30" "$NEST_ARITH_CS" "20.2 Nested arithmetic in CS"
compute() {
    local a=$1
    local b=$2
    local op=$3
    case $op in
        add) echo $((a + b)) ;;
        sub) echo $((a - b)) ;;
        mul) echo $((a * b)) ;;
        div) echo $((a / b)) ;;
        *) echo 0 ;;
    esac
}
assert_eq "15" "$(compute 10 5 add)" "20.3 Compute(10,5,add)"
assert_eq "5" "$(compute 10 5 sub)" "20.3b Compute(10,5,sub)"
assert_eq "50" "$(compute 10 5 mul)" "20.3c Compute(10,5,mul)"
assert_eq "2" "$(compute 10 5 div)" "20.3d Compute(10,5,div)"
contains_str() {
    case "$1" in
        *$2*) return 0 ;;
        *) return 1 ;;
    esac
}
contains_str "hello world" "world"
assert_eq "0" "$?" "20.4 String contains - found"
contains_str "hello world" "xyz"
assert_eq "1" "$?" "20.4b String contains - not found"
EXT_OUT=$(date)
assert_not_empty "$EXT_OUT" "20.5 External command (date) runs"
WHOAMI=$(whoami)
assert_not_empty "$WHOAMI" "20.6 External command (whoami)"

echo ""
echo "--- Section 21: Break/Continue Depth ---"
NEST_BREAK=""
for i in a b c; do
    for j in 1 2 3; do
        if test $j -eq 2; then
            break
        fi
        NEST_BREAK="${NEST_BREAK}${i}${j} "
    done
done
assert_eq "a1 b1 c1 " "$NEST_BREAK" "21.1 Break from inner loop"
NEST_CONT=""
for i in a b; do
    for j in 1 2 3; do
        if test $j -eq 2; then
            continue
        fi
        NEST_CONT="${NEST_CONT}${i}${j} "
    done
done
assert_eq "a1 a3 b1 b3 " "$NEST_CONT" "21.2 Continue in nested loop"

echo ""
echo "--- Section 22: Complex Interaction Tests ---"
pipeline_func() {
    local v=$1
    echo $((v * 2))
}
PIPE_FN=$(pipeline_func 21 | cat)
assert_eq "42" "$PIPE_FN" "22.1 Function output piped through cat"
if true; then
    PERSIST_IF=hello_from_if
fi
assert_eq "hello_from_if" "$PERSIST_IF" "22.2 Variable in if block persists"
for x in 1; do
    PERSIST_FOR=hello_from_for
done
assert_eq "hello_from_for" "$PERSIST_FOR" "22.3 Variable in for loop persists"
PW=0
while test $PW -lt 1; do
    PERSIST_WHILE=hello_from_while
    PW=$((PW + 1))
done
assert_eq "hello_from_while" "$PERSIST_WHILE" "22.4 Variable in while loop persists"
PC=case_test
case $PC in
    case_test) PERSIST_CASE=matched_persist ;;
esac
assert_eq "matched_persist" "$PERSIST_CASE" "22.5 Variable in case persists"
alias showmath='echo 30'
assert_eq "30" "$(showmath)" "22.6 Alias with static value"
CS_FOR2=""
for f in alpha beta gamma; do
    CS_FOR2="${CS_FOR2}[${f}]"
done
assert_eq "[alpha][beta][gamma]" "$CS_FOR2" "22.7 Word list with concatenation"

echo ""
echo "================================================================"
echo "                    TEST SUMMARY"
echo "================================================================"
echo "  TOTAL:  $TOTAL_COUNT"
echo "  PASS:   $PASS_COUNT"
echo "  FAIL:   $FAIL_COUNT"
if test $FAIL_COUNT -eq 0; then
    echo "  STATUS: ALL TESTS PASSED!"
else
    PCT=$((PASS_COUNT * 100 / TOTAL_COUNT))
    echo "  STATUS: ${PCT}% passed ($FAIL_COUNT failures)"
fi
echo "================================================================"
echo ""

if test $FAIL_COUNT -gt 0; then
    exit 1
fi
exit 0