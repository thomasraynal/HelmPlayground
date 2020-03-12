{{- define "api-name" -}}
{{- .Values.app | replace "." "-" -}}
{{- end -}}

{{- define "namespace" -}}
{{- .Values.group | replace "." "-" -}}
{{- end -}}

{{- define "api-chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}