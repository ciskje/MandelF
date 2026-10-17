"""Benchmark Esperto: firma a 64 bit + verifica (tamper-evident).

Il codice lega il risultato misurato (rendering/s) al nome hardware e agli
altri campi del test. Scopo: rilevare il ritocco di uno screenshot (qualunque
cifra modificata invalida il codice). NON e' una prova assoluta contro un
falsario che ricalcola il codice col programma modificato (app open-source e
offline: impossibile senza un verificatore terzo) — ed e' documentato cosi'
anche nel dialog di verifica.
Formato (16 hex, gruppi da 4): rps quantizzato (24 bit, centesimi) +
impronta hw (16 bit di SHA256 del nome normalizzato) + checksum (24 bit di
SHA256 sulla stringa canonica di tutti i campi). Nessun segreto incorporato.
"""
import hashlib

_RPS_BITS = 24
_HW_BITS = 16
_CHK_BITS = 24
_RPS_SCALE = 100  # centesimi di rendering/s
_RPS_MAX = (1 << _RPS_BITS) - 1


def norm_hw(name):
    """Normalizza il nome hardware (case-insensitive, spazi compattati)."""
    return " ".join(str(name).split()).lower()


def _canon(rps_q, hw, backend, prec, version, mi, secs):
    return "|".join([str(int(rps_q)), hw, str(backend), str(prec),
                     str(version).strip(), str(int(mi)), str(float(secs))])


def _digest(data):
    return hashlib.sha256(data.encode("utf-8")).digest()


def make_code(rps, hw_name, backend, prec, version, mi, secs):
    """Firma a 64 bit -> 16 cifre hex maiuscole (senza trattini)."""
    rps_q = min(_RPS_MAX, max(0, int(round(float(rps) * _RPS_SCALE))))
    hw = norm_hw(hw_name)
    hw_fp = int.from_bytes(_digest(hw)[:2], "big") & ((1 << _HW_BITS) - 1)
    chk = int.from_bytes(
        _digest(_canon(rps_q, hw, backend, prec, version, mi, secs))[:3],
        "big") & ((1 << _CHK_BITS) - 1)
    val = (rps_q << (_HW_BITS + _CHK_BITS)) | (hw_fp << _CHK_BITS) | chk
    return "%016X" % val


def parse_code(code):
    """(rps_q, hw_fp, chk) da 16 hex (trattini/spazi tollerati). ValueError se malformato."""
    s = "".join(str(code).split()).replace("-", "").upper()
    if len(s) != 16 or any(c not in "0123456789ABCDEF" for c in s):
        raise ValueError("formato codice non valido (servono 16 cifre hex)")
    val = int(s, 16)
    chk = val & ((1 << _CHK_BITS) - 1)
    hw_fp = (val >> _CHK_BITS) & ((1 << _HW_BITS) - 1)
    rps_q = val >> (_HW_BITS + _CHK_BITS)
    return rps_q, hw_fp, chk


def fmt_code(code):
    """16 hex -> gruppi da 4 (XXXX-XXXX-XXXX-XXXX) per il dialog."""
    s = "".join(str(code).split()).replace("-", "").upper()
    return "-".join(s[i:i + 4] for i in range(0, 16, 4))


def verify_code(code, hw_name, backend, prec, version, mi, secs):
    """(verdetto, rps, dettaglio). Verdetti: OK / HW DIVERSO / MANOMESSO /
    FORMATO INVALIDO. `hw_name` e' digitato dall'utente (letto dallo screenshot)."""
    try:
        rps_q, hw_fp, chk = parse_code(code)
    except ValueError as ex:
        return ("FORMATO INVALIDO", None, str(ex))
    rps = rps_q / _RPS_SCALE
    hw = norm_hw(hw_name)
    if not hw:
        return ("HW DIVERSO", rps, "nome hardware vuoto")
    hw_fp2 = int.from_bytes(_digest(hw)[:2], "big") & ((1 << _HW_BITS) - 1)
    if hw_fp2 != hw_fp:
        return ("HW DIVERSO", rps,
                "impronta %04X, nel codice %04X: nome digitato male o "
                "screenshot di un'altra macchina" % (hw_fp2, hw_fp))
    chk2 = int.from_bytes(
        _digest(_canon(rps_q, hw, backend, prec, version, mi, secs))[:3],
        "big") & ((1 << _CHK_BITS) - 1)
    if chk2 != chk:
        return ("MANOMESSO", rps,
                "checksum %06X, nel codice %06X: almeno un campo "
                "(rps, motore, versione, mi, secs) non corrisponde" % (chk2, chk))
    return ("OK", rps, "codice coerente con tutti i campi")
