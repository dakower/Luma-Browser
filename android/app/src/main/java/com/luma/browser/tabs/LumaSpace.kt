package com.luma.browser.tabs

data class LumaSpace(
    val id: String,
    val name: String,
    val icon: String,
    val color: String
) {
    companion object {
        fun defaultSpaces(): List<LumaSpace> = listOf(
            LumaSpace("main", "Основное", "ic_home", "#7468C7"),
            LumaSpace("work", "Работа", "ic_tabs", "#4FACFE"),
            LumaSpace("study", "Учёба", "ic_book_open", "#38EF7D"),
            LumaSpace("media", "Медиа", "ic_spark", "#FF5E97")
        )
    }
}
