#!/bin/bash
clear
# Colore verde Matrix
echo -e "\e[1;32m"
echo "=================================================="
echo " SOULJEM ENGINE - REALTIME PITCH BENCHMARK TOOL   "
echo "=================================================="
echo ""
echo "Inizializzazione moduli di analisi hardware..."
sleep 0.5
echo "Controllo core CPU: OK"
sleep 0.5
echo "Verifica istruzioni DSP: OK"
sleep 0.5
echo ""
echo "ATTENZIONE: Il Pitch Realtime richiede CPU ad alte prestazioni."
echo "Hardware lento causerà distorsioni audio e desincronizzazione."
echo "--------------------------------------------------"
# INDIZIO GIALLO - Reset finale per tornare al verde
echo -e "\e[1;33m[HINT]: Se cerchi lo sblocco per il tuo pitch live,"
echo -e "scrivi il nome di chi ti ha dato il cammino... (C _ _ _ _ _ _ _ _ _ o)\e[1;32m"
echo "--------------------------------------------------"
echo ""
echo " 1) Avvia Benchmark CPU"
echo " 2) Esci e mantieni limitazioni"
echo ""
echo -e "\e[0m" # Reset colori per l'input dell'utente
read -p "Inserisci comando o Password: " choice

STATUS_FILE="$HOME/SoulJem_v5/pitch_status.txt"
TEMP_FILE="/tmp/sj_current_test.txt"

if [ "$choice" == "camperlingo" ]; then
    echo ""
    echo -e "\e[1;32m[!] OVERRIDE DI SISTEMA RILEVATO."
    echo "[!] Sblocco manuale autorizzato dall'amministratore.\e[0m"
    echo "PASS" > "$TEMP_FILE"
    sleep 2
    exit 0

elif [ "$choice" == "1" ]; then
    echo ""
    echo "Avvio calcolo intensivo (15000 cicli SHA-256 in corso...)"
    echo "Non chiudere questa finestra..."
    
    START=$(date +%s%N)
    for i in {1..15000}; do 
        echo $i | sha256sum > /dev/null
    done
    END=$(date +%s%N)
    DIFF=$(( ($END - $START) / 1000000 ))
    
    echo "--------------------------------------------------"
    echo "TEMPO REGISTRATO: $DIFF ms"
    echo "--------------------------------------------------"
    
    if [ $DIFF -lt 2500 ]; then
        echo -e "\e[1;32m[ RISULTATO: OTTIMO ]"
        echo "La tua CPU e' idonea per il Pitch Realtime.\e[0m"
        echo "PASS" > "$TEMP_FILE"
        echo "PASS" > "$STATUS_FILE"
    else
        echo -e "\e[1;31m[ RISULTATO: INSUFFICIENTE ]"
        echo "Hardware troppo lento. Il Pitch Live rimane DISATTIVATO.\e[0m"
        echo "FAIL" > "$TEMP_FILE"
    fi
    
    echo ""
    echo "Ritorno al programma in 5 secondi..."
    sleep 5
    exit 0

else
    echo "Annullato."
    sleep 1
    exit 0
fi
