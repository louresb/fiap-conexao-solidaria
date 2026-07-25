{{- define "conexao-solidaria.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "conexao-solidaria.fullname" -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "conexao-solidaria.labels" -}}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ include "conexao-solidaria.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: conexao-solidaria
cs.fiap.io/cloud-provider: {{ .Values.global.cloudProvider | quote }}
cs.fiap.io/environment: {{ .Values.global.environment | quote }}
{{- end -}}

{{- define "conexao-solidaria.selectorLabels" -}}
app.kubernetes.io/name: {{ include "conexao-solidaria.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "conexao-solidaria.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "conexao-solidaria.fullname" .) .Values.serviceAccount.name -}}
{{- else -}}
{{- default "default" .Values.serviceAccount.name -}}
{{- end -}}
{{- end -}}

{{- define "conexao-solidaria.image" -}}
{{- $registry := trimSuffix "/" .root.Values.image.registry -}}
{{- if $registry -}}
{{- printf "%s/%s:%s" $registry .component.imageRepository .root.Values.image.tag -}}
{{- else -}}
{{- printf "%s:%s" .component.imageRepository .root.Values.image.tag -}}
{{- end -}}
{{- end -}}
