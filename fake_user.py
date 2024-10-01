#!/usr/bin/python
# -*- coding: utf-8 -*-
# AD にランダムのユーザを 100人作成する by Copilot
#
# Microsoft Store で Python 3.12 をインストール (3.12.6)
# https://www.python.org/downloads/release/python-3126/ からダウンロード ＆ インストールも可
#
# MeCab のインストール (UTF-8 を選択してインストール)
# https://taku910.github.io/mecab/#download
# ln -s /etc/mecabrc /usr/local/etc/mecabrc
#
# 必要なモジュールの追加
# dnf install MeCab mecab-ipadic
# pip install faker pykakasi mecab-python3
#
# スクリプトの実行
# python ./fake_user.py
#
# 部署の OU を作成する
# New-ADOrganizationalUnit -Name '営業部' -Path "DC=my,DC=home"
# New-ADOrganizationalUnit -Name '開発部' -Path "DC=my,DC=home"
# New-ADOrganizationalUnit -Name '総務部' -Path "DC=my,DC=home"
# New-ADOrganizationalUnit -Name '人事部' -Path "DC=my,DC=home"
# New-ADOrganizationalUnit -Name '財務部' -Path "DC=my,DC=home"
# New-ADOrganizationalUnit -Name 'マーケティング部' -Path "DC=my,DC=home"
# New-ADOrganizationalUnit -Name '法務部' -Path "DC=my,DC=home"
# New-ADOrganizationalUnit -Name 'IT部' -Path "DC=my,DC=home"
# New-ADOrganizationalUnit -Name '研究開発部' -Path "DC=my,DC=home"
# New-ADOrganizationalUnit -Name '品質管理部' -Path "DC=my,DC=home"
#
# https://hirotanoblog.com/windows-active-directory-powershell-aduser/12450/
# 出力ファイルを読み込み、ユーザを追加 (PowerShell)
# Import-Csv -Path "C:\Users\Administrator\fakeuser_data.csv" | ForEach-Object {
#     New-ADUser -name $_.name `
#                -DisplayName $_.name `
#                -SamAccountName $_.sam `
#                -UserPrincipalName $_.email `
#                -Path $_.ou `
#                -AccountPassword (ConvertTo-SecureString -AsPlainText $_.pass -force) `
#                -Enabled 1
#     }
#
#  $_.name  : ユーザーの名前
#  $_.sam   : ユーザーのSamAccountName (ログインID)
#  $_.email : ユーザーのUserPrincipalName（メールアドレス）
#  $_.ou    : ユーザーを作成するOUのパス
#  $_.pass  : パスワード

import string
import csv
import random
from faker import Faker
import MeCab
import pykakasi

fake = Faker('ja_JP')
kks = pykakasi.kakasi()
mecab = MeCab.Tagger("-Owakati")

departments = ['営業部', '開発部', '総務部', '人事部', '財務部', 'マーケティング部', '法務部', 'IT部', '研究開発部', '品質管理部']

def to_romanized_name(name):
    words = mecab.parse(name).strip().split()
    romanized_name = ''.join([kks.convert(word)[0]['hepburn'] for word in words])
    return romanized_name.replace("'", "").replace('"', '')

def generate_login_name(first_name, last_name):
    first_name_romanized = to_romanized_name(first_name).lower()
    last_name_romanized = to_romanized_name(last_name).lower()
    return f"{first_name_romanized}.{last_name_romanized}"

def generate_password(length=20):
    characters = string.ascii_letters + string.digits + string.punctuation
    password = ''.join(random.choice(characters) for i in range(length))
    return password

with open('fakeuser_data.csv', mode='w', newline='') as file:
    writer = csv.writer(file)
    #       名前, メールアドレス, ログインID, OU, パスワード
    writer.writerow(['name', 'email', 'sam', 'ou', 'pass'])

    for i in range(100):
        last_name = fake.last_name()
        first_name = fake.first_name()
        login_name = generate_login_name(first_name, last_name)
        email = f"{login_name}@my.home"
        department = random.choice(departments)
        password = generate_password()
        writer.writerow([f"{last_name} {first_name}", email, login_name, f'OU={department},DC=my,DC=home', password])

print("CSVファイルが生成されました。")
