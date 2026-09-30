// Intentionally unguarded: a shader may have both shared and per-pass programs.
// Disable standard Unity fog before its lighting includes are compiled. This
// also covers URP versions using dynamic fog keywords instead of fog variants.
#undef FOG_LINEAR
#undef FOG_EXP
#undef FOG_EXP2
#undef FOG_LINEAR_KEYWORD_DECLARED
#undef FOG_EXP_KEYWORD_DECLARED
#undef FOG_EXP2_KEYWORD_DECLARED
