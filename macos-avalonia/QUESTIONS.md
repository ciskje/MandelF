# QUESTIONS (per autore SPEC) (per autore SPEC)

1. `AutoIterForScale(9.36)`: testo dice ≈1944, la formula dà 1011
   (1.5/4.68=0.3205, log10=−0.494, 2000×0.506=1011). Implementata la formula
   (benchmark 10915 torna esatto). Quale valore è corretto?
2. Early-out, esempio `c = 0.3`: è fuori dal cardioid (max 0.25) e diverge entro
   256 iter → non nero. Implementata matematica esatta. Esempio da correggere?
3. Vista iniziale `scale = 9.36` vs `re in [-2.68, 1.68]` (larghezza 4.36):
   implementato scale come larghezza complessa. Range da correggere?
